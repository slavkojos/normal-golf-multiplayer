using UnityEngine;

namespace NormalGolfMultiplayer.Remote
{
    internal partial class RemotePlayerView
    {
        private Transform _elbowL, _elbowR, _handL;
        private const float UpperArmLength = 0.29f, ForearmLength = 0.29f;

        private static Mesh Profile(string name, params Vector4[] rings) => Visuals.ProfileMesh("Golfer_" + name, rings);
        private static Vector4 Ring(float height, float width, float depth, float offset = 0f) => new Vector4(height, width, depth, offset);
        private static Transform Shape(string name, PrimitiveType primitive, Transform parent, Vector3 position, Vector3 size,
            Color color, Visuals.Surface surface = Visuals.Surface.Skin, Quaternion? rotation = null) =>
            Visuals.Detail(name, Visuals.GetMesh(primitive), parent, position, size, color, surface, rotation);

        private void BuildDetailedAvatar()
        {
            _scaler = Visuals.Pivot("Scaler", transform, Vector3.zero);
            _legL = Visuals.Pivot("LegL", _scaler, new Vector3(-0.115f, 0.95f, 0f));
            _legR = Visuals.Pivot("LegR", _scaler, new Vector3(0.115f, 0.95f, 0f));
            _kneeL = BuildDetailedLeg(_legL, out _ankleL);
            _kneeR = BuildDetailedLeg(_legR, out _ankleR);
            _torso = Visuals.Pivot("Torso", _scaler, new Vector3(0f, 0.95f, 0f));
            Visuals.Detail("TailoredTrousers", Profile("Hips", Ring(-0.055f, 0.195f, 0.122f), Ring(0.06f, 0.218f, 0.138f), Ring(0.13f, 0.205f, 0.125f)), _torso, Vector3.zero, Vector3.one, Visuals.Pants);
            Colored(Visuals.Detail("FittedPolo", Profile("Polo", Ring(0.12f, 0.207f, 0.13f), Ring(0.20f, 0.20f, 0.125f), Ring(0.35f, 0.22f, 0.143f), Ring(0.49f, 0.237f, 0.15f), Ring(0.575f, 0.227f, 0.13f), Ring(0.64f, 0.09f, 0.073f)), _torso, Vector3.zero, Vector3.one, Color.white));
            Visuals.Detail("LeatherBelt", Profile("Belt", Ring(0.108f, 0.214f, 0.138f), Ring(0.14f, 0.214f, 0.138f)), _torso, Vector3.zero, Vector3.one, Visuals.Hair, Visuals.Surface.Leather);
            Shape("BeltBuckle", PrimitiveType.Cube, _torso, new Vector3(0f, 0.124f, 0.141f), new Vector3(0.048f, 0.033f, 0.013f), Visuals.Steel, Visuals.Surface.Metal);
            Shape("BuckleInset", PrimitiveType.Cube, _torso, new Vector3(0f, 0.124f, 0.149f), new Vector3(0.028f, 0.019f, 0.003f), Visuals.Hair, Visuals.Surface.Leather);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape("BeltLoop", PrimitiveType.Cube, _torso, new Vector3(side * 0.145f, 0.127f, 0.107f), new Vector3(0.018f, 0.056f, 0.016f), Visuals.Pants, Visuals.Surface.Cloth);
                Visuals.Strip("PocketWelt", _torso, new Vector3(side * 0.17f, 0.085f, 0.089f), new Vector3(side * 0.195f, -0.018f, 0.074f), 0.0028f, Visuals.Pants * 0.7f);
                var collar = Visuals.Detail("FoldedCollar", Visuals.CollarMesh(side), _torso, Vector3.zero, Vector3.one, Color.white);
                DarkColored(collar);
            }
            DarkColored(Shape("PoloPlacket", PrimitiveType.Cube, _torso, new Vector3(0f, 0.548f, 0.146f), new Vector3(0.027f, 0.12f, 0.012f), Color.white, Visuals.Surface.Cloth));
            for (int i = 0; i < 3; i++) Shape("PoloButton", PrimitiveType.Sphere, _torso, new Vector3(0f, 0.585f - i * 0.034f, 0.156f), Vector3.one * 0.011f, Visuals.Shoes, Visuals.Surface.Leather);
            Shape("ChestEmblem", PrimitiveType.Sphere, _torso, new Vector3(-0.131f, 0.46f, 0.14f), new Vector3(0.031f, 0.031f, 0.006f), Visuals.Shoes, Visuals.Surface.Cloth);

            _head = Visuals.Pivot("Head", _torso, new Vector3(0f, 0.645f, 0f));
            BuildDetailedHead();
            _swing = Visuals.Pivot("Swing", _torso, new Vector3(0f, 0.575f, 0f));
            _armL = BuildDetailedArm("ArmL", -0.235f, true, out _elbowL, out _handL);
            _armR = BuildDetailedArm("ArmR", 0.235f, false, out _elbowR, out _handR);
            _hands = Visuals.Pivot("Hands", _swing, new Vector3(0f, -0.46f, 0.24f));
            _club = Visuals.Pivot("Club", _hands, Vector3.zero);
            Shape("TexturedGrip", PrimitiveType.Cylinder, _club, new Vector3(0f, -0.09f, 0f), new Vector3(0.024f, 0.095f, 0.024f), Visuals.Dark, Visuals.Surface.Leather);
            for (int i = 0; i < 9; i++) Shape("GripRib", PrimitiveType.Cylinder, _club, new Vector3(0f, -0.02f - i * 0.018f, 0f), new Vector3(0.026f, 0.002f, 0.026f), Visuals.Hair, Visuals.Surface.Leather);
            Shape("SteelShaft", PrimitiveType.Cylinder, _club, new Vector3(0f, -0.58f, 0f), new Vector3(0.014f, 0.4f, 0.014f), Visuals.Steel, Visuals.Surface.Metal);
            Shape("Hosel", PrimitiveType.Cylinder, _club, new Vector3(0f, -0.97f, 0.008f), new Vector3(0.023f, 0.03f, 0.023f), Visuals.Steel, Visuals.Surface.Metal);
            Shape("IronHead", PrimitiveType.Cube, _club, new Vector3(0f, -1f, 0.045f), new Vector3(0.025f, 0.049f, 0.105f), Visuals.Steel, Visuals.Surface.Metal, Quaternion.Euler(0f, 0f, -18f));
            for (int i = 0; i < 5; i++) Visuals.Strip("FaceGroove", _club, new Vector3(0.013f, -1.016f + i * 0.007f, 0.009f), new Vector3(0.013f, -1.016f + i * 0.007f, 0.087f), 0.0006f, Visuals.Dark);
            _clubParent = _hands;
            Visuals.OptimizeAvatar(_scaler, _colorParts, _darkColorParts);
            _nameTag = Visuals.Label("NameTag", transform, 3f, Color.white);
            _nameTag.transform.localPosition = new Vector3(0f, 2.22f, 0f);
        }

        private void BuildDetailedHead()
        {
            Shape("Neck", PrimitiveType.Cylinder, _head, new Vector3(0f, 0.035f, -0.006f), new Vector3(0.105f, 0.048f, 0.102f), Visuals.Skin);
            Visuals.Detail("AnatomicalHead", Profile("Head", Ring(0.065f, 0.06f, 0.059f, 0.012f), Ring(0.095f, 0.092f, 0.082f, 0.009f), Ring(0.14f, 0.113f, 0.101f), Ring(0.195f, 0.127f, 0.114f), Ring(0.25f, 0.126f, 0.118f, -0.007f), Ring(0.30f, 0.107f, 0.102f, -0.01f), Ring(0.334f, 0.047f, 0.055f, -0.012f)), _head, Vector3.zero, Vector3.one, Visuals.Skin, Visuals.Surface.Skin);
            Color lip = new Color(0.65f, 0.38f, 0.31f), ear = new Color(0.77f, 0.52f, 0.42f);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape("Ear", PrimitiveType.Sphere, _head, new Vector3(side * 0.127f, 0.19f, -0.008f), new Vector3(0.04f, 0.072f, 0.046f), Visuals.Skin);
                Shape("EarConcha", PrimitiveType.Sphere, _head, new Vector3(side * 0.143f, 0.19f, 0.002f), new Vector3(0.006f, 0.041f, 0.024f), ear);
                Shape("EyeSocket", PrimitiveType.Sphere, _head, new Vector3(side * 0.052f, 0.217f, 0.096f), new Vector3(0.049f, 0.025f, 0.027f), ear);
                Shape("Eye", PrimitiveType.Sphere, _head, new Vector3(side * 0.052f, 0.217f, 0.108f), new Vector3(0.034f, 0.015f, 0.013f), new Color(0.88f, 0.85f, 0.79f));
                Shape("Iris", PrimitiveType.Sphere, _head, new Vector3(side * 0.049f, 0.217f, 0.116f), new Vector3(0.011f, 0.012f, 0.005f), new Color(0.22f, 0.28f, 0.24f));
                Shape("Pupil", PrimitiveType.Sphere, _head, new Vector3(side * 0.049f, 0.217f, 0.119f), new Vector3(0.005f, 0.007f, 0.003f), Visuals.Dark);
                Visuals.Strip("UpperEyelid", _head, new Vector3(side * 0.033f, 0.225f, 0.112f), new Vector3(side * 0.07f, 0.223f, 0.10f), 0.0025f, Visuals.Skin);
                Visuals.Strip("Eyebrow", _head, new Vector3(side * 0.029f, 0.246f, 0.108f), new Vector3(side * 0.078f, 0.238f, 0.093f), 0.004f, Visuals.Hair);
                Shape("NostrilWing", PrimitiveType.Sphere, _head, new Vector3(side * 0.017f, 0.174f, 0.123f), new Vector3(0.024f, 0.022f, 0.022f), Visuals.Skin);
                Shape("Nostril", PrimitiveType.Sphere, _head, new Vector3(side * 0.014f, 0.167f, 0.133f), new Vector3(0.009f, 0.005f, 0.008f), ear);
                Shape("Sideburn", PrimitiveType.Cube, _head, new Vector3(side * 0.117f, 0.237f, -0.018f), new Vector3(0.013f, 0.047f, 0.049f), Visuals.Hair, Visuals.Surface.Cloth);
            }
            Shape("NoseBridge", PrimitiveType.Sphere, _head, new Vector3(0f, 0.205f, 0.115f), new Vector3(0.029f, 0.074f, 0.048f), Visuals.Skin);
            Shape("NoseTip", PrimitiveType.Sphere, _head, new Vector3(0f, 0.179f, 0.14f), new Vector3(0.036f, 0.027f, 0.029f), Visuals.Skin);
            Shape("UpperLip", PrimitiveType.Sphere, _head, new Vector3(0f, 0.142f, 0.107f), new Vector3(0.054f, 0.01f, 0.017f), lip);
            Shape("LowerLip", PrimitiveType.Sphere, _head, new Vector3(0f, 0.133f, 0.108f), new Vector3(0.05f, 0.011f, 0.018f), lip);
            Visuals.Strip("LipLine", _head, new Vector3(-0.023f, 0.138f, 0.114f), new Vector3(0.023f, 0.138f, 0.114f), 0.001f, ear * 0.65f);
            Shape("Chin", PrimitiveType.Sphere, _head, new Vector3(0f, 0.102f, 0.066f), new Vector3(0.083f, 0.044f, 0.05f), Visuals.Skin);
            Shape("Hairline", PrimitiveType.Sphere, _head, new Vector3(0f, 0.282f, -0.032f), new Vector3(0.256f, 0.15f, 0.205f), Visuals.Hair, Visuals.Surface.Cloth);
            Colored(Visuals.Detail("SixPanelCap", Profile("Cap", Ring(0.282f, 0.132f, 0.124f, -0.006f), Ring(0.32f, 0.134f, 0.13f, -0.006f), Ring(0.365f, 0.097f, 0.098f, -0.01f), Ring(0.39f, 0.018f, 0.023f, -0.009f)), _head, Vector3.zero, Vector3.one, Color.white));
            DarkColored(Visuals.Detail("CapBand", Profile("CapBand", Ring(0.279f, 0.134f, 0.126f, -0.006f), Ring(0.291f, 0.134f, 0.126f, -0.006f)), _head, Vector3.zero, Vector3.one, Color.white));
            DarkColored(Shape("CurvedVisor", PrimitiveType.Sphere, _head, new Vector3(0f, 0.283f, 0.139f), new Vector3(0.28f, 0.018f, 0.19f), Color.white, Visuals.Surface.Cloth, Quaternion.Euler(8f, 0f, 0f)));
            Shape("CapBadge", PrimitiveType.Sphere, _head, new Vector3(0f, 0.326f, 0.125f), new Vector3(0.022f, 0.027f, 0.006f), Visuals.Shoes, Visuals.Surface.Cloth);
            Colored(Shape("CapButton", PrimitiveType.Sphere, _head, new Vector3(0f, 0.392f, -0.01f), new Vector3(0.016f, 0.009f, 0.016f), Color.white, Visuals.Surface.Cloth));
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                for (int segment = 0; segment < 3; segment++)
                {
                    float a = segment / 3f, b = (segment + 1) / 3f;
                    Vector3 p0 = new Vector3(Mathf.Cos(angle) * Mathf.Lerp(0.132f, 0.018f, a), Mathf.Lerp(0.30f, 0.39f, a), Mathf.Sin(angle) * Mathf.Lerp(0.129f, 0.021f, a) - 0.006f);
                    Vector3 p1 = new Vector3(Mathf.Cos(angle) * Mathf.Lerp(0.132f, 0.018f, b), Mathf.Lerp(0.30f, 0.39f, b), Mathf.Sin(angle) * Mathf.Lerp(0.129f, 0.021f, b) - 0.006f);
                    DarkColored(Visuals.Strip("CapSeam", _head, p0, p1, 0.0012f, Color.white));
                }
            }
        }

        private static Transform BuildDetailedLeg(Transform leg, out Transform ankle)
        {
            Visuals.Strip("TrouserCrease", leg, new Vector3(0f, -0.08f, 0.126f), new Vector3(0f, -0.41f, 0.087f), 0.0017f, Visuals.Pants * 0.85f);
            var knee = Visuals.Pivot("Knee", leg, new Vector3(0f, -0.44f, 0f));
            Visuals.SkinnedLimb("ContinuousTrousers", Profile("SkinnedLeg", Ring(-0.87f, 0.071f, 0.067f), Ring(-0.78f, 0.079f, 0.073f), Ring(-0.64f, 0.092f, 0.085f, -0.012f), Ring(-0.44f, 0.083f, 0.086f), Ring(-0.30f, 0.096f, 0.105f), Ring(-0.15f, 0.108f, 0.122f), Ring(0.095f, 0.112f, 0.125f)), leg, knee, -0.44f, Visuals.Pants, Visuals.Surface.Cloth);
            Visuals.Detail("TrouserHem", Profile("Hem", Ring(-0.429f, 0.073f, 0.071f), Ring(-0.407f, 0.075f, 0.071f)), knee, Vector3.zero, Vector3.one, Visuals.Pants * 0.88f);
            ankle = Visuals.Pivot("Ankle", knee, new Vector3(0f, -0.46f, 0f));
            Shape("Sock", PrimitiveType.Cylinder, ankle, new Vector3(0f, 0.026f, -0.015f), new Vector3(0.112f, 0.034f, 0.104f), Visuals.Shoes, Visuals.Surface.Cloth);
            Shape("RubberOutsole", PrimitiveType.Sphere, ankle, new Vector3(0f, -0.03f, 0.066f), new Vector3(0.163f, 0.042f, 0.29f), Visuals.Dark, Visuals.Surface.Leather);
            Shape("Midsole", PrimitiveType.Sphere, ankle, new Vector3(0f, -0.013f, 0.066f), new Vector3(0.16f, 0.043f, 0.285f), Visuals.Shoes, Visuals.Surface.Leather);
            Shape("LeatherShoe", PrimitiveType.Sphere, ankle, new Vector3(0f, 0.018f, 0.06f), new Vector3(0.152f, 0.092f, 0.27f), Visuals.Shoes, Visuals.Surface.Leather);
            Shape("HeelCounter", PrimitiveType.Sphere, ankle, new Vector3(0f, 0.026f, -0.052f), new Vector3(0.149f, 0.089f, 0.085f), Visuals.Pants, Visuals.Surface.Leather);
            Shape("Tongue", PrimitiveType.Sphere, ankle, new Vector3(0f, 0.058f, 0.035f), new Vector3(0.067f, 0.024f, 0.115f), Visuals.Pants, Visuals.Surface.Leather);
            for (int i = 0; i < 5; i++) Visuals.Strip("Lace", ankle, new Vector3(-0.034f, 0.058f, -0.002f + i * 0.018f), new Vector3(0.034f, 0.058f, 0.008f + i * 0.018f), 0.0023f, Visuals.Shoes);
            return knee;
        }

        private Transform BuildDetailedArm(string name, float x, bool glove, out Transform elbow, out Transform hand)
        {
            var arm = Visuals.Pivot(name, _swing, new Vector3(x, 0f, 0f));
            Colored(Visuals.Detail("PoloSleeve", Profile("Sleeve", Ring(-0.19f, 0.077f, 0.079f), Ring(-0.10f, 0.087f, 0.088f), Ring(0f, 0.092f, 0.094f), Ring(0.06f, 0.063f, 0.066f), Ring(0.079f, 0.014f, 0.016f)), arm, Vector3.zero, Vector3.one, Color.white));
            DarkColored(Visuals.Detail("SleeveHem", Profile("SleeveHem", Ring(-0.198f, 0.079f, 0.081f), Ring(-0.178f, 0.079f, 0.081f)), arm, Vector3.zero, Vector3.one, Color.white));
            elbow = Visuals.Pivot("Elbow", arm, new Vector3(0f, -UpperArmLength, 0f));
            Visuals.SkinnedLimb("ContinuousArm", Profile("SkinnedArm", Ring(-0.58f, 0.037f, 0.038f), Ring(-0.51f, 0.043f, 0.045f), Ring(-0.40f, 0.058f, 0.063f), Ring(-0.29f, 0.057f, 0.060f), Ring(-0.18f, 0.068f, 0.073f), Ring(-0.04f, 0.079f, 0.078f)), arm, elbow, -UpperArmLength, Visuals.Skin, Visuals.Surface.Skin);
            hand = Visuals.Pivot("Hand", elbow, new Vector3(0f, -ForearmLength, 0f));
            Color handColor = glove ? Visuals.Shoes : Visuals.Skin;
            Shape("Palm", PrimitiveType.Sphere, hand, new Vector3(0f, -0.035f, 0f), new Vector3(0.081f, 0.09f, 0.044f), handColor, Visuals.Surface.Leather);
            for (int finger = 0; finger < 4; finger++)
            {
                float fingerX = (finger - 1.5f) * 0.018f;
                float length = finger == 0 || finger == 3 ? 0.047f : 0.062f;
                Shape("Finger", PrimitiveType.Capsule, hand, new Vector3(fingerX, -0.079f - length * 0.22f, 0.008f), new Vector3(0.016f, length * 0.5f, 0.018f), handColor, Visuals.Surface.Leather, Quaternion.Euler(-18f, 0f, 0f));
                Shape("Knuckle", PrimitiveType.Sphere, hand, new Vector3(fingerX, -0.062f, -0.018f), Vector3.one * 0.019f, handColor);
            }
            Shape("Thumb", PrimitiveType.Capsule, hand, new Vector3(x < 0 ? 0.042f : -0.042f, -0.032f, 0.017f), new Vector3(0.024f, 0.032f, 0.024f), handColor, Visuals.Surface.Leather, Quaternion.Euler(-28f, 0f, x < 0 ? 28f : -28f));
            if (glove)
            {
                Shape("GloveCuff", PrimitiveType.Cylinder, hand, Vector3.zero, new Vector3(0.081f, 0.013f, 0.076f), Visuals.Shoes, Visuals.Surface.Leather);
                Shape("GloveClosure", PrimitiveType.Cube, hand, new Vector3(0f, -0.023f, -0.027f), new Vector3(0.045f, 0.03f, 0.006f), Visuals.Pants, Visuals.Surface.Leather);
            }
            return arm;
        }
    }
}
