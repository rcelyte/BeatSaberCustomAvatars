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
using System.Linq;
using CustomAvatar.Logging;
using CustomAvatar.Tracking;
using UniGLTF;
using UnityEngine;
using UnityEngine.XR.Hands;
using Zenject;
using Object = UnityEngine.Object;

namespace CustomAvatar.Avatar
{
    /// <summary>
    /// Allows spawning instances of <see cref="AvatarPrefab"/>.
    /// </summary>
    public class AvatarSpawner
    {
        private readonly DiContainer _container;
        private readonly ILogger<AvatarSpawner> _logger;

        private readonly List<(Type type, Func<AvatarPrefab, bool> condition)> _componentsToAdd = new();

        private protected AvatarSpawner(DiContainer container, ILogger<AvatarSpawner> logger)
        {
            _container = container;
            _logger = logger;

            RegisterComponent<AvatarTransformTracking>(avatar => avatar.head || avatar.leftHand || avatar.rightHand || avatar.pelvis || avatar.leftLeg || avatar.rightLeg);
            RegisterComponent<AvatarIK>(avatar => avatar.isIKAvatar);
            RegisterComponent<AvatarFaceTracking>(avatar => avatar.supportsFaceTracking);
        }

        public void RegisterComponent<T>(Func<AvatarPrefab, bool> condition = null) where T : MonoBehaviour
        {
            if (IsComponentRegistered<T>()) throw new InvalidOperationException("Registering the same component more than once is not supported");

            _componentsToAdd.Add((typeof(T), condition));
        }

        public bool IsComponentRegistered<T>()
        {
            return _componentsToAdd.Any(vt => vt.type == typeof(T));
        }

        public void DeregisterComponent<T>() where T : MonoBehaviour
        {
            _componentsToAdd.RemoveAll(vt => vt.type == typeof(T));
        }

        /// <summary>
        /// Spawn a <see cref="AvatarPrefab"/>.
        /// </summary>
        /// <param name="avatar">The <see cref="AvatarPrefab"/> to spawn</param>
        /// <param name="input">The <see cref="IAvatarInput"/> to use</param>
        /// <param name="parent">The container in which to spawn the avatar (optional)</param>
        /// <returns><see cref="SpawnedAvatar"/></returns>
        public SpawnedAvatar SpawnAvatar(AvatarPrefab avatar, IAvatarInput input, Transform parent = null)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            if (input == null) throw new ArgumentNullException(nameof(input));

            if (parent)
            {
                _logger.LogInformation($"Spawning avatar '{avatar.descriptor.name}' into '{parent.name}'");
            }
            else
            {
                _logger.LogInformation($"Spawning avatar '{avatar.descriptor.name}'");
            }

            GameObject avatarInstance = Object.Instantiate(avatar, parent, false).gameObject;
            Object.DestroyImmediate(avatarInstance.GetComponent<AvatarPrefab>());
            Object.DestroyImmediate(avatarInstance.GetComponentInChildren<RuntimeGltfInstance>());

            DiContainer subContainer = new(_container);
            subContainer.Bind<AvatarPrefab>().FromInstance(avatar);
            subContainer.Bind<IAvatarInput>().FromInstance(input);

            // SpawnedAvatar needs to be instantiated first since other behaviours depend on it
            SpawnedAvatar spawnedAvatar = subContainer.InstantiateComponent<SpawnedAvatar>(avatarInstance);
            spawnedAvatar.avatarFormat = avatar.avatarFormat;

            subContainer.Bind<SpawnedAvatar>().FromInstance(spawnedAvatar);

            foreach (Type type in from entry in _componentsToAdd where (entry.condition == null || entry.condition(avatar)) select entry.type)
            {
                _logger.LogInformation($"Adding component '{type.FullName}'");
                avatarInstance.AddComponent(type);
            }

            if (avatar.supportsFingerTracking)
            {
                Animator animator = avatarInstance.GetComponentInChildren<Animator>();
                foreach ((GameObject hand, bool right) in new[] {(spawnedAvatar.leftHand.gameObject, false), (spawnedAvatar.rightHand.gameObject, true)})
                {
                    hand.AddComponent<XRHandTrackingEvents>().handedness = right ? Handedness.Right : Handedness.Left;
                    Transform wrist = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                    // Transform palm = animator.GetBoneTransform(right ? HumanBodyBones.RightPalm : HumanBodyBones.LeftPalm);
                    Transform thumbProximal = animator.GetBoneTransform(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
                    Transform thumbDistal = animator.GetBoneTransform(right ? HumanBodyBones.RightThumbDistal : HumanBodyBones.LeftThumbDistal);
                    // Transform thumbTip = animator.GetBoneTransform(right ? HumanBodyBones.RightThumbTip : HumanBodyBones.LeftThumbTip);
                    Transform indexProximal = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                    Transform indexIntermediate = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexIntermediate : HumanBodyBones.LeftIndexIntermediate);
                    Transform indexDistal = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexDistal : HumanBodyBones.LeftIndexDistal);
                    // Transform indexTip = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexTip : HumanBodyBones.LeftIndexTip);
                    Transform middleProximal = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                    Transform middleIntermediate = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleIntermediate : HumanBodyBones.LeftMiddleIntermediate);
                    Transform middleDistal = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleDistal : HumanBodyBones.LeftMiddleDistal);
                    // Transform middleTip = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleTip : HumanBodyBones.LeftMiddleTip);
                    Transform ringProximal = animator.GetBoneTransform(right ? HumanBodyBones.RightRingProximal : HumanBodyBones.LeftRingProximal);
                    Transform ringIntermediate = animator.GetBoneTransform(right ? HumanBodyBones.RightRingIntermediate : HumanBodyBones.LeftRingIntermediate);
                    Transform ringDistal = animator.GetBoneTransform(right ? HumanBodyBones.RightRingDistal : HumanBodyBones.LeftRingDistal);
                    // Transform ringTip = animator.GetBoneTransform(right ? HumanBodyBones.RightRingTip : HumanBodyBones.LeftRingTip);
                    Transform littleProximal = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                    Transform littleIntermediate = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleIntermediate : HumanBodyBones.LeftLittleIntermediate);
                    Transform littleDistal = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleDistal : HumanBodyBones.LeftLittleDistal);
                    // Transform littleTip = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleTip : HumanBodyBones.LeftLittleTip);
                    XRHandSkeletonDriver skeletonDriver = hand.AddComponent<XRHandSkeletonDriver>();
                    skeletonDriver.rootTransform = wrist;
                    skeletonDriver.jointTransformReferences = new(new JointToTransformReference[] {
                        new() {xrHandJointID = XRHandJointID.Wrist, jointTransform = wrist},
                        // new() {xrHandJointID = XRHandJointID.Palm, jointTransform = palm},
                        // new() {xrHandJointID = XRHandJointID.ThumbMetacarpal, jointTransform = },
                        new() {xrHandJointID = XRHandJointID.ThumbProximal, jointTransform = thumbProximal},
                        new() {xrHandJointID = XRHandJointID.ThumbDistal, jointTransform = thumbDistal},
                        // new() {xrHandJointID = XRHandJointID.ThumbTip, jointTransform = thumbTip},
                        // new() {xrHandJointID = XRHandJointID.IndexMetacarpal, jointTransform = },
                        new() {xrHandJointID = XRHandJointID.IndexProximal, jointTransform = indexProximal},
                        new() {xrHandJointID = XRHandJointID.IndexIntermediate, jointTransform = indexIntermediate},
                        new() {xrHandJointID = XRHandJointID.IndexDistal, jointTransform = indexDistal},
                        // new() {xrHandJointID = XRHandJointID.IndexTip, jointTransform = indexTip},
                        // new() {xrHandJointID = XRHandJointID.MiddleMetacarpal, jointTransform = },
                        new() {xrHandJointID = XRHandJointID.MiddleProximal, jointTransform = middleProximal},
                        new() {xrHandJointID = XRHandJointID.MiddleIntermediate, jointTransform = middleIntermediate},
                        new() {xrHandJointID = XRHandJointID.MiddleDistal, jointTransform = middleDistal},
                        // new() {xrHandJointID = XRHandJointID.MiddleTip, jointTransform = middleTip},
                        // new() {xrHandJointID = XRHandJointID.RingMetacarpal, jointTransform = },
                        new() {xrHandJointID = XRHandJointID.RingProximal, jointTransform = ringProximal},
                        new() {xrHandJointID = XRHandJointID.RingIntermediate, jointTransform = ringIntermediate},
                        new() {xrHandJointID = XRHandJointID.RingDistal, jointTransform = ringDistal},
                        // new() {xrHandJointID = XRHandJointID.RingTip, jointTransform = ringTip},
                        // new() {xrHandJointID = XRHandJointID.LittleMetacarpal, jointTransform = },
                        new() {xrHandJointID = XRHandJointID.LittleProximal, jointTransform = littleProximal},
                        new() {xrHandJointID = XRHandJointID.LittleIntermediate, jointTransform = littleIntermediate},
                        new() {xrHandJointID = XRHandJointID.LittleDistal, jointTransform = littleDistal},
                        // new() {xrHandJointID = XRHandJointID.LittleTip, jointTransform = littleTip},
                    });
                }
            }

            if (spawnedAvatar.avatarFormat == AvatarPrefab.AvatarFormat.AVATAR_FORMAT_VRM && spawnedAvatar.ik == null && spawnedAvatar.TryGetComponent(out AvatarIK ik))
            {
                spawnedAvatar.VRM_SetAvatarIK(ik);
            }

            subContainer.InjectGameObject(avatarInstance);
            avatarInstance.SetActive(true);

            return spawnedAvatar;
        }
    }
}
