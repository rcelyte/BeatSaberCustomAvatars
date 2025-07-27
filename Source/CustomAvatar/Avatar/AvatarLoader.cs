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
using UniVRM10;
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

                avatarPrefab = _container.InstantiateComponent<AvatarPrefab>(prefabObject, new object[] {AvatarPrefab.AvatarFormat.AVATAR_FORMAT_CUSTOM});

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

            // TODO: Port MToon10 to Beat Saber lighting
            typeof(VRM10.MToon10.MToon10Meta).GetField("UnityShaderName", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, VRM.BuiltInVrmMToonMaterialImporter.FallbackShaders["VRM/UnlitTexture"]);

            static IMaterialDescriptorGenerator materialCallback(VRM.glTF_VRM_extensions vrm) =>
                VrmMaterialDescriptorGeneratorUtility.GetValidVrmMaterialDescriptorGenerator(vrm);
            _logger.LogInformation("Vrm: loading.");
            RuntimeOnlyAwaitCaller awaitCaller = new();
            Vrm10Instance vrm10Instance = null;
            MonoBehaviour instance;
            using (GltfData gltfData = await awaitCaller.Run(() => new GlbLowLevelParser(path, File.ReadAllBytes(path)).Parse()))
            {
                if (UniGLTF.Extensions.VRMC_vrm.GltfDeserializer.TryGet(gltfData.GLTF.extensions, out UniGLTF.Extensions.VRMC_vrm.VRMC_vrm _))
                {
                    vrm10Instance = await Vrm10.LoadGltfDataAsync(gltfData, canLoadVrm0X: false, showMeshes: true, awaitCaller: awaitCaller);
                    await vrm10Instance.Vrm.FirstPerson.SetupAsync(vrm10Instance.gameObject, awaitCaller, true,
                        CustomAvatar.Avatar.AvatarLayers.kAlwaysVisible, CustomAvatar.Avatar.AvatarLayers.kOnlyInThirdPerson);
                    instance = vrm10Instance;
                }
                else if (glTF_VRM_extensions.TryDeserialize(gltfData.GLTF.extensions, out glTF_VRM_extensions _))
                {
                    VRMData vrm = new(gltfData);
                    using (VRMImporterContext loader = new(vrm, materialGenerator: materialCallback(vrm.VrmExtension)))
                    {
                        RuntimeGltfInstance vrm0Instance = await loader.LoadAsync(awaitCaller);
                        vrm0Instance.ShowMeshes();
                        instance = vrm0Instance;
                    }
                }
                else
                {
                    throw new AvatarLoadException("No VRM extension present in GLB file");
                }
            }

            Animator animator = instance.GetComponent<Animator>();
            GameObject avatar = new("Avatar");
            avatar.SetActive(false);

            {
                _logger.LogInformation("New VRM Avatar");
                GameObject.DontDestroyOnLoad(avatar);

                instance.transform.SetParent(avatar.transform, false);

                VRIKManager ik = instance.gameObject.AddComponent<VRIKManager>();
                ik.AutoDetectReferences();

                Transform vrmFirstPersonHeadBone;
                Vector3 vrmFirstPersonOffset;
                if (vrm10Instance == null)
                {
                    VRMFirstPerson firstPerson = instance.GetComponent<VRMFirstPerson>();
                    firstPerson.Setup();
                    vrmFirstPersonHeadBone = firstPerson.FirstPersonBone;
                    vrmFirstPersonOffset = firstPerson.FirstPersonOffset;
                }
                else if (vrm10Instance.TryGetBoneTransform(HumanBodyBones.Head, out vrmFirstPersonHeadBone))
                {
                    vrmFirstPersonOffset = vrm10Instance.Vrm.LookAt.OffsetFromHead;
                }
                else
                {
                    throw new AvatarLoadException("Failed to get head bone for VRM");
                }

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

                GameObject head = new("Head");
                head.transform.SetParent(avatar.transform);
                head.transform.position = ik.references_head.position;// = vrmFirstPersonHeadBone.position + vrmFirstPersonOffset;

                GameObject headViewpoint = new("HeadViewPoint");
                headViewpoint.transform.SetParent(head.transform);
                headViewpoint.transform.position = vrmFirstPersonHeadBone.position - vrmFirstPersonOffset;

                ik.solver_spine_headTarget = headViewpoint.transform;

                AvatarDescriptor descriptor = avatar.AddComponent<AvatarDescriptor>();
                descriptor.name = "";
                descriptor.author = "";
                descriptor.cover = null;
                if (vrm10Instance != null)
                {
                    VRM10ObjectMeta meta = vrm10Instance.Vrm.Meta;
                    descriptor.name = meta.Name;
                    descriptor.author = String.Join(", ", meta.Authors);
                    if (meta.Thumbnail != null)
                        descriptor.cover = Sprite.Create(meta.Thumbnail, new Rect(0, 0, meta.Thumbnail.width, meta.Thumbnail.height), Vector2.zero);
                }
                else if (instance.TryGetComponent(out VRMMeta meta))
                {
                    descriptor.name = meta.Meta.Title;
                    descriptor.author = meta.Meta.Author;
                    if (meta.Meta.Thumbnail != null)
                        descriptor.cover = Sprite.Create(meta.Meta.Thumbnail, new Rect(0, 0, meta.Meta.Thumbnail.width, meta.Meta.Thumbnail.height), Vector2.zero);
                }

                if (descriptor.name.Length == 0)
                    descriptor.name = System.IO.Path.GetFileName(path);
            }

            AvatarPrefab avatarPrefab = _container.InstantiateComponent<AvatarPrefab>(avatar, new object[] {AvatarPrefab.AvatarFormat.AVATAR_FORMAT_VRM});
            avatarPrefab.name = $"AvatarPrefab({avatarPrefab.descriptor.name})";

            _tasks.Remove(path);

            return avatarPrefab;
        }
    }
}
