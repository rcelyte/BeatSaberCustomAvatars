//  Beat Saber Custom Avatars - Custom player models for body presence in Beat Saber.
//  Copyright © 2018-2025  Nicolas Gnyra and Beat Saber Custom Avatars Contributors
//
//  This library is free software: you can redistribute it and/or
//  modify it under the terms of the GNU Lesser General Public
//  License as published by the Free Software Foundation, either
//  version 3 of the License, or (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.

//#define USE_VRM_10
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssetBundleLoadingTools.Utilities;
using CustomAvatar.Exceptions;
using CustomAvatar.Logging;
using CustomAvatar.Utilities;
using CustomAvatar.VRMAvatar;
using IPA.Utilities;
using IPA.Utilities.Async;
using UniGLTF;
using UnityEngine;
#if USE_VRM_10
using UniVRM10;
#endif
using VRM;
using Zenject;

namespace CustomAvatar.Avatar
{
    /// <summary>
    /// Allows loading <see cref="AvatarPrefab"/> from various sources.
    /// </summary>
    public class AvatarLoader
    {
        private const string kGameObjectName = "_CustomAvatar";

        private readonly AssetLoader _assetLoader;
        private readonly ILogger<AvatarLoader> _logger;
        private readonly DiContainer _container;

        private readonly Dictionary<string, Task<AvatarPrefab>> _tasks = [];

        private protected AvatarLoader(AssetLoader assetLoader, ILogger<AvatarLoader> logger, DiContainer container)
        {
            _assetLoader = assetLoader;
            _logger = logger;
            _container = container;
        }

        /// <summary>
        /// Load an avatar from a file.
        /// </summary>
        /// <param name="path">Path to the .avatar file.</param>
        /// <returns>A <see cref="Task{T}"/> that completes once the avatar has loaded.</returns>
        public Task<AvatarPrefab> LoadFromFileAsync(string path)
        {
            return LoadFromFileAsync(path, null, CancellationToken.None);
        }

        /// <summary>
        /// Load an avatar from a file.
        /// </summary>
        /// <param name="path">Path to the .avatar file.</param>
        /// <param name="progress">The <see cref="IProgress{T}"/> to use to report loading progress.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task{T}"/> that completes once the avatar has loaded.</returns>
        public Task<AvatarPrefab> LoadFromFileAsync(string path, IProgress<float> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!UnityGame.OnMainThread) throw new InvalidOperationException($"{nameof(LoadFromFileAsync)} should only be called on the main thread");

            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                throw new IOException($"File '{fullPath}' does not exist");
            }

            // prevent Unity from complaining that we're loading the same asset bundle more than once concurrently
            if (_tasks.TryGetValue(fullPath, out Task<AvatarPrefab> task))
            {
                return task;
            }

            _logger.LogInformation($"Loading avatar from '{fullPath}'");

            if (Path.GetExtension(fullPath) == ".vrm")
            {
                task = LoadVRM(fullPath, progress, cancellationToken);
                //_tasks.Add(fullPath, task); //reload avatar from cache not working atm for some reason.
            }
            else
            {
                task = LoadAssetBundle(fullPath, progress, cancellationToken);
                _tasks.Add(fullPath, task);
            }
            return task;
        }

        private async Task<AvatarPrefab> LoadAssetBundle(string fullPath, IProgress<float> progress, CancellationToken cancellationToken)
        {
            AssetBundle assetBundle = null;
            AvatarPrefab avatarPrefab = null;

            try
            {
                AssetBundleCreateRequest assetBundleCreateRequest = AssetBundle.LoadFromFileAsync(fullPath);

                if (progress != null)
                {
                    // this isn't amazing, but since there's no progress event and Unity expects the progress
                    // property will be accessed in an Update() loop, this should *hopefully* be fine
                    _ = UnityMainThreadTaskScheduler.Factory.StartNew(async () =>
                    {
                        while (assetBundleCreateRequest.progress < 1f)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            progress.Report(assetBundleCreateRequest.progress);
                            await Task.Yield();
                        }

                        progress.Report(1);
                    }, cancellationToken);
                }

                // for the time being, we don't allow cancelling the actual loading because of the possibility of multiple places
                // waiting for the same task to complete due to the task reuse in LoadFromFileAsync - some kind of cancellation
                // token merging would be required to avoid cancelling the task if it is being awaited from somewhere else

                await assetBundleCreateRequest;
                assetBundle = assetBundleCreateRequest.assetBundle;

                if (!assetBundle)
                {
                    throw new AvatarLoadException("Could not load asset bundle");
                }

                AssetBundleRequest assetBundleRequest = await assetBundle.LoadAssetWithSubAssetsAsync<GameObject>(kGameObjectName);
                GameObject prefabObject = (GameObject)assetBundleRequest.asset;

                if (!prefabObject)
                {
                    throw new AvatarLoadException("Could not load asset from asset bundle");
                }

                avatarPrefab = _container.InstantiateComponent<AvatarPrefab>(prefabObject);

                prefabObject.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                prefabObject.name = $"AvatarPrefab({avatarPrefab.descriptor.name})";
                prefabObject.SetActive(false);

                await ShaderRepair.FixShadersOnGameObjectAsync(prefabObject);

                prefabObject.hideFlags &= ~HideFlags.DontUnloadUnusedAsset;

                return avatarPrefab;
            }
            finally
            {
                if (assetBundle != null)
                {
                    await assetBundle.UnloadAsync(avatarPrefab == null);
                }

                _tasks.Remove(fullPath);
            }
        }

        private async Task<AvatarPrefab> LoadVRM(string path, IProgress<float> progress, CancellationToken cancellationToken)
        {
            VRM.VRMFirstPerson.FIRSTPERSON_ONLY_LAYER = CustomAvatar.Avatar.AvatarLayers.kAlwaysVisible;
            VRM.VRMFirstPerson.THIRDPERSON_ONLY_LAYER = CustomAvatar.Avatar.AvatarLayers.kOnlyInThirdPerson;

            if (!await _assetLoader.vrmShaderLoad)
            {
                throw new AvatarLoadException("Could not load internal VRM shaders");
            }

            VRM.BuiltInVrmMToonMaterialImporter.FallbackShaders["VRM/MToon"] = "BeatSaber/MToon";
            #if USE_VRM_10 //NOTE: Cannot use as VRM1.0 requires Shader MToon10, which has not yet been converted to Beatsaber [and thus is white-out'ed].
            VRM.BuiltInVrmMToonMaterialImporter.FallbackShaders["VRM10/MToon10"] = VRM.BuiltInVrmMToonMaterialImporter.FallbackShaders["VRM/UnlitTexture"];
            #endif

#if USE_VRM_10 //NOTE: Cannot use as VRM1.0 requires Shader MToon10, which has not yet been converted to Beatsaber [and thus is white-out'ed].
            _logger.LogWarning("Vrm1.0: loading.");
            Vrm10.LoadPathAsync(path); 
            Vrm10Instance instance = await Vrm10.LoadPathAsync(path);
#else
            Debug.LogWarning("Vrm0.x: loading.");

            static IMaterialDescriptorGenerator materialCallback(VRM.glTF_VRM_extensions vrm) =>
                VrmMaterialDescriptorGeneratorUtility.GetValidVrmMaterialDescriptorGenerator(vrm);
            RuntimeGltfInstance instance = await VrmUtility.LoadAsync(path, new RuntimeOnlyAwaitCaller(), materialCallback);
#endif
            //await ShaderRepair.FixShadersOnGameObjectAsync(instance.gameObject);

            Animator animator = instance.GetComponent<Animator>();

            GameObject avatar = new("Avatar");

            {
                Debug.LogWarning("New VRM Avatar");
                GameObject.DontDestroyOnLoad(avatar);

                instance.transform.SetParent(avatar.transform, false);
#if USE_VRM_10
#else
                instance.ShowMeshes();
#endif

                VRIKManager ik = instance.gameObject.AddComponent<VRIKManager>();
                ik.AutoDetectReferences();

                VRMFirstPerson firstPerson = instance.GetComponent<VRMFirstPerson>();
                firstPerson.Setup();

                GameObject leftHand = new("LeftHand");
                leftHand.transform.SetParent(avatar.transform);
                GameObject rightHand = new("RightHand");
                rightHand.transform.SetParent(avatar.transform);

                GameObject leftHandTarget = new("LeftHandTarget");
                //adjust hand and wrist locations [wrt Saber Stick]
                leftHandTarget.transform.SetParent(leftHand.transform);
                leftHandTarget.transform.eulerAngles = new Vector3(-10f, 0f, 90f); //rotate wrist to standard natural angle.
                leftHandTarget.transform.position = VRMHandAndLegPositionConstants.GetWrist(ik.references_leftHand, false); //curl fingers
                ik.solver_leftArm_target = leftHandTarget.transform;

                GameObject rightHandTarget = new("RightHandTarget");
                //adjust hand and wrist locations [wrt Saber Stick]
                rightHandTarget.transform.SetParent(rightHand.transform);
                rightHandTarget.transform.eulerAngles = new Vector3(-10f, 0f, -90f); //rotate wrist to standard natural angle.
                rightHandTarget.transform.position = VRMHandAndLegPositionConstants.GetWrist(ik.references_rightHand, true); //get wrist position. then curl fingers.
                ik.solver_rightArm_target = rightHandTarget.transform;

                Transform vrmFirstPersonHeadBone = firstPerson.FirstPersonBone;
                Vector3 vrmFirstPersonOffset = firstPerson.FirstPersonOffset;

                GameObject head = new("Head");
                head.transform.SetParent(avatar.transform);
                head.transform.position = ik.references_head.position;// = vrmFirstPersonHeadBone.position + vrmFirstPersonOffset;

                GameObject headViewpoint = new("HeadViewPoint");
                headViewpoint.transform.SetParent(head.transform);
                headViewpoint.transform.position = vrmFirstPersonHeadBone.position - vrmFirstPersonOffset;

                ik.solver_spine_headTarget = headViewpoint.transform;

                AvatarDescriptor descriptor = avatar.AddComponent<AvatarDescriptor>();
                if (instance.TryGetComponent(out VRMMeta meta))
                {
                    descriptor.name = meta.Meta.Title;
                    descriptor.author = meta.Meta.Author;
                    if (meta.Meta.Thumbnail != null)
                        descriptor.cover = Sprite.Create(meta.Meta.Thumbnail, new Rect(0, 0, meta.Meta.Thumbnail.width, meta.Meta.Thumbnail.height), Vector2.zero);
                    if (descriptor.name.Length == 0)
                        descriptor.name = "";
                }
                else
                {
                    descriptor.name = "";
                    descriptor.author = "";
                    descriptor.cover = null;
                }

                if (descriptor.name == "")
                    descriptor.name = System.IO.Path.GetFileName(path);
            }

            AvatarPrefab avatarPrefab = _container.InstantiateComponent<AvatarPrefab>(avatar);
            avatarPrefab.name = $"AvatarPrefab({avatarPrefab.descriptor.name})";
            avatarPrefab.gameObject.SetActive(false); //set the AvatarPrefab as Not Active [instantiated avatars will be set as active].

            _tasks.Remove(path);

            return avatarPrefab;
        }
    }
}
