using UnityEngine;

namespace CustomAvatar.VRMAvatar
{
    public class VRMHandAndLegPositionConstants
    {
        public struct Finger
        {
            public Quaternion knuckleOne;
            public Quaternion knuckleTwo;
            public Quaternion knuckleThree;

            public Finger(Quaternion knuckleOne, Quaternion knuckleTwo, Quaternion knuckleThree)
            {
                this.knuckleOne = knuckleOne;
                this.knuckleTwo = knuckleTwo;
                this.knuckleThree = knuckleThree;
            }
        }

        public static Finger index = new(
            new Quaternion(0.008440408f, -0.001334298f, 0.2419432f, 0.9702529f),
            new Quaternion(0.006469311f, -0.005582907f, 0.7170327f, 0.6969872f),
            new Quaternion(0.005353101f, -0.006660718f, 0.8312484f, 0.5558357f));
        public static Finger little = new(
            new Quaternion(0.008255607f, -0.00205866f, 0.241947f, 0.9702522f),
            new Quaternion(0.005930441f, -0.006101066f, 0.7170366f, 0.6969836f),
            new Quaternion(0.004364784f, -0.007303547f, 0.8583599f, 0.5129775f));
        public static Finger middle = new(
            new Quaternion(0.008255607f, -0.00205866f, 0.241947f, 0.9702522f),
            new Quaternion(0.005930441f, -0.006101066f, 0.7170366f, 0.6969836f),
            new Quaternion(0.001539939f, -0.0083679f, 0.98344f, 0.1809834f));
        public static Finger ring = new(
            new Quaternion(0.008255607f, -0.00205866f, 0.241947f, 0.9702522f),
            new Quaternion(0.005930441f, -0.006101066f, 0.7170366f, 0.6969836f),
            new Quaternion(0.002387176f, -0.008166672f, 0.9597998f, 0.2805563f));
        public static Finger thumb = new(
            new Quaternion(0.5142931f, -0.0385387f, -0.02314265f, 0.8564355f),
            new Quaternion(0.502104f, -0.1893172f, -0.1136858f, 0.8361376f),
            new Quaternion(0.4902184f, -0.2618173f, -0.1572224f, 0.8163448f));
        private static Quaternion Reflect(Quaternion quat, bool bReflect)
        {
            if (!bReflect)
            {
                return quat;
            }
            else
            {
                Quaternion temp = quat;
                temp.z = -temp.z;
                temp.y = -temp.y;
                return temp;
            }
        }

        public static Vector3 GetWrist(Transform hand, bool bRightHand)
        {
            Vector3 offsetBetweenWristAndGrip = new();
            if (hand == null)
                return new Vector3(); //nothing can be done.

            //NOTE: Hand rotation can't be done here as the Tracking Device is mapped directly to the hand, overwriting rotations.

            int nFingers = 5;
            if (hand.childCount < nFingers)
                nFingers = hand.childCount;

            float magnitude_hand_to_finger = 0.04f; //default:4cm.
            float magnitude_indexfinger_beforeAfterRotation = 0.03f;//default: 3cm.
            float magnitude_first_last_fingers = 0.11f; //default 11cm.

            for (int i = 0; i < nFingers; i++)
            {
                Transform finger = hand.GetChild(i); //knuckle 1

                if (finger == null)
                    continue; //nothing can be done.

                Transform knuckleTwo = finger.GetChild(0);
                Transform knuckleThree = knuckleTwo.GetChild(0);

                Finger fingerThing = new(Quaternion.identity, Quaternion.identity, Quaternion.identity);
                switch (i)
                {
                    case 0:
                        magnitude_hand_to_finger = (hand.position - finger.position).magnitude;
                        fingerThing = index;
                        break;
                    case 1:
                        fingerThing = little;
                        break;
                    case 2:
                        fingerThing = middle;
                        break;
                    case 3:
                        fingerThing = ring;
                        break;
                    case 4:
                        fingerThing = thumb;
                        break;
                    default:
                        break;
                }

                Vector3 before = knuckleThree ? knuckleThree.position : new Vector3(0.0f, 0.0f, 0.0f);
                finger.rotation = Reflect(fingerThing.knuckleOne, bRightHand); //kuckle 1
                if (knuckleTwo)
                    knuckleTwo.rotation = Reflect(fingerThing.knuckleTwo, bRightHand); //knuckle 2
                if (knuckleThree)
                    knuckleThree.rotation = Reflect(fingerThing.knuckleThree, bRightHand); //knuckle 3
                Vector3 after = knuckleThree ? knuckleThree.position : new Vector3(0.0f, 0.0f, 0.03f);
                if (i == 0)
                    magnitude_indexfinger_beforeAfterRotation = (before - after).magnitude;

                if (i == nFingers - 1/*last is thumb*/)
                {
                    magnitude_first_last_fingers = (hand.GetChild(0).position - hand.GetChild(i).position).magnitude;
                }
            }

            /*calculate center of grip*/
            float sign = bRightHand ? 1 : -1;
            offsetBetweenWristAndGrip.x = sign * (magnitude_indexfinger_beforeAfterRotation / 2);
            offsetBetweenWristAndGrip.y = 0.75f * magnitude_hand_to_finger;
            offsetBetweenWristAndGrip.z = -magnitude_first_last_fingers;

            return offsetBetweenWristAndGrip;
        }

        public static Vector3 ApplyToHand(Transform hand, bool bRightHand)
        {
            Vector3 offsetBetweenWristAndGrip = new();
            if (hand == null)
                return new Vector3(); //nothing can be done.

            //NOTE: Hand rotation can't be done here as the Tracking Device is mapped directly to the hand, overwriting rotations.

            int nFingers = 5;
            if (hand.childCount < nFingers)
                nFingers = hand.childCount;

            float magnitude_hand_to_finger = 0.04f; //default:4cm.
            float magnitude_indexfinger_beforeAfterRotation = 0.03f;//default: 3cm.
            float magnitude_first_last_fingers = 0.11f; //default 11cm.

            for (int i = 0; i < nFingers; i++)
            {
                Transform finger = hand.GetChild(i); //knuckle 1

                if (finger == null)
                    continue; //nothing can be done.

                Transform knuckleTwo = finger.GetChild(0);
                Transform knuckleThree = knuckleTwo.GetChild(0);

                Finger fingerThing = new(Quaternion.identity, Quaternion.identity, Quaternion.identity);
                switch (i)
                {
                    case 0:
                        magnitude_hand_to_finger = (hand.position - finger.position).magnitude;
                        fingerThing = index;
                        break;
                    case 1:
                        fingerThing = little;
                        break;
                    case 2:
                        fingerThing = middle;
                        break;
                    case 3:
                        fingerThing = ring;
                        break;
                    case 4:
                        fingerThing = thumb;
                        break;
                    default:
                        break;
                }

                Vector3 before = knuckleThree ? knuckleThree.position : new Vector3(0.0f, 0.0f, 0.0f);
                finger.rotation = Reflect(fingerThing.knuckleOne, bRightHand); //kuckle 1
                if (knuckleTwo)
                    knuckleTwo.rotation = Reflect(fingerThing.knuckleTwo, bRightHand); //knuckle 2
                if (knuckleThree)
                    knuckleThree.rotation = Reflect(fingerThing.knuckleThree, bRightHand); //knuckle 3
                Vector3 after = knuckleThree ? knuckleThree.position : new Vector3(0.0f, 0.0f, 0.03f);
                if (i == 0)
                    magnitude_indexfinger_beforeAfterRotation = (before - after).magnitude;

                if (i == nFingers - 1/*last is thumb*/)
                {
                    magnitude_first_last_fingers = (hand.GetChild(0).position - hand.GetChild(i).position).magnitude;
                }
            }

            /*calculate center of grip*/
            float sign = bRightHand ? 1 : -1;
            offsetBetweenWristAndGrip.x = sign * (magnitude_indexfinger_beforeAfterRotation / 2);
            offsetBetweenWristAndGrip.y = 0.75f * magnitude_hand_to_finger;
            offsetBetweenWristAndGrip.z = -magnitude_first_last_fingers;

            return offsetBetweenWristAndGrip;
        }

        public static void InitIK_AvatarHandAndLegs(VRIKManager ik)
        {
            ApplyToHand(ik.references_leftHand, false);
            ApplyToHand(ik.references_rightHand, true);

            ik.references_rightThigh.transform.eulerAngles = new Vector3(0f, 35f, 0);
            ik.references_leftThigh.transform.eulerAngles = new Vector3(0f, -35f, 0f);
        }
    }
}
