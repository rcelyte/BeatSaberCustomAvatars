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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CustomAvatar.Logging;
using CustomAvatar.Tracking;
using UnityEngine;
using Zenject;

namespace CustomAvatar.Player
{
    internal class GazeNode : ITrackedNode
    {
        public Transform offset => null;
        public bool isTracking => false;
        public bool isCalibrated => false;
    }

    /// <summary>
    /// The player's <see cref="IAvatarInput"/> with calibration and other settings applied.
    /// </summary>
    internal class VRPlayerInput : IInitializable, IDisposable, IAvatarInput
    {
        private readonly ILogger<VRPlayerInput> _logger;
        private readonly TrackingRig _trackingRig;
        private readonly GazeNode _gaze = new();
        private readonly Dictionary<string, float> _shapeWeights = new();
        private CancellationTokenSource _oscCancellationTokenSource;

        protected VRPlayerInput(
            ILogger<VRPlayerInput> logger,
            TrackingRig trackingRig)
        {
            _logger = logger;
            _trackingRig = trackingRig;
        }

        public event Action inputChanged;

        public void Initialize()
        {
            _oscCancellationTokenSource = new();
            Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 9000));
                _logger.LogInformation($"Bound port UDP/9000");
                HandleOsc(socket, _oscCancellationTokenSource.Token);
            }
            catch (SocketException ex)
            {
                _logger.LogError($"Could not bind to recv endpoint: {ex.Message}");
            }
            _trackingRig.trackingChanged += OnTrackingRigChanged;
        }

        public void Dispose()
        {
            _trackingRig.trackingChanged -= OnTrackingRigChanged;
            _oscCancellationTokenSource?.Cancel();
        }

        private async void HandleOsc(Socket socket, CancellationToken cancellationToken)
        {
            void Process(ReadOnlySpan<byte> packet) {
                foreach (OscMessage message in new OscPacket(packet))
                {
                    if (message.length != 1 || message[0].asFloat is not float value)
                    {
                        continue;
                    }
                    string key = Char.ToUpper((char)message.address[1]) + Encoding.UTF8.GetString(message.address.Slice(2));
                    if (!_shapeWeights.ContainsKey(key))
                    {
                        _logger.LogInformation($"New OSC value: `{key}`");
                    }
                    _shapeWeights[key] = value;
                }
            }
            byte[] buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                int length = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                try
                {
                    Process(new(buffer, 0, length));
                }
                catch (Exception) {}
            }
        }

        public bool TryGetTransform(DeviceUse use, out Transform transform)
        {
            ITrackedNode node = use switch
            {
                DeviceUse.Head => _trackingRig.head,
                DeviceUse.LeftHand => _trackingRig.leftHand,
                DeviceUse.RightHand => _trackingRig.rightHand,
                DeviceUse.Waist => _trackingRig.pelvis,
                DeviceUse.LeftFoot => _trackingRig.leftFoot,
                DeviceUse.RightFoot => _trackingRig.rightFoot,
                DeviceUse.Gaze => _gaze,
                _ => throw new InvalidOperationException($"Unexpected device use {use}"),
            };

            transform = node.offset;

            return node.isTracking && node.isCalibrated;
        }

        private void OnTrackingRigChanged()
        {
            inputChanged?.Invoke();
        }

        public IReadOnlyDictionary<string, float> shapeWeights => _shapeWeights;
    }
}
