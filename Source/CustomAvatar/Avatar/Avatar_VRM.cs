using UnityEngine;

namespace CustomAvatar.VRMAvatar
{
    public class VRMHandAndLegPositionConstants
    {
        public static void InitIK_AvatarHandAndLegs(VRIKManager ik)
        {
            ik.references_rightThigh.transform.eulerAngles = new Vector3(0f, 35f, 0);
            ik.references_leftThigh.transform.eulerAngles = new Vector3(0f, -35f, 0f);
        }
    }
}
