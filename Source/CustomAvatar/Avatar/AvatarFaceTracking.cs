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

using CustomAvatar.Tracking;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
// using VRM;
using Zenject;

namespace CustomAvatar.Avatar
{
    [DisallowMultipleComponent]
    public class AvatarFaceTracking : MonoBehaviour
    {
        // Vrm10RuntimeLookAt
        // private VRMBlendShapeProxy _blendShapes;
        private ILookup<string, (SkinnedMeshRenderer, int)> _blendShapes;
        private IAvatarInput _input;

        [Inject]
        [UsedImplicitly]
        private void Construct(IAvatarInput input)
        {
            _input = input;
        }

        protected void Start()
        {
            // _blendShapes = GetComponentInChildren<VRMBlendShapeProxy>();
            _blendShapes = GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(renderer => {
                Mesh mesh = renderer.sharedMesh;
                return Enumerable.Range(0, mesh.blendShapeCount).Select(index => (mesh.GetBlendShapeName(index), renderer, index));
            }).ToLookup(entry => entry.Item1, entry => (entry.renderer, entry.index));
        }

        protected void LateUpdate()
        {
            if (_input == null || _blendShapes == null)
            {
                return;
            }

            /*foreach (KeyValuePair<string, float> weights in _input.shapeWeights)
            {
                _blendShapes.ImmediatelySetValue(BlendShapeKey.CreateUnknown(weights.Key), weights.Value);
            }*/

            foreach (KeyValuePair<string, float> weight in _input.shapeWeights)
            {
                foreach ((SkinnedMeshRenderer renderer, int index) in _blendShapes[weight.Key])
                {
                    renderer.SetBlendShapeWeight(index, weight.Value * UniVRM10.MorphTargetBinding.VRM_TO_UNITY);
                }
            }
        }

        /*private void OnInputChanged()
        {
            if (_avatarInput.TryGetTransform(DeviceUse.Gaze, out Transform target))
            {
                LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
                LookAtTarget = target;
            }
        }*/
    }
}
