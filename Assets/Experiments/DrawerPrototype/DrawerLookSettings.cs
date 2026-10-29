using UnityEngine;

namespace WeatherHouse.DrawerPrototype
{
    public sealed class DrawerLookSettings : ScriptableObject
    {
        public Material[] grass = new Material[3];
        public Material ground, paper, paperFold, magicBook, dictionary, orangeFlowers, letterPaper;
        public float density = 28.011204f;
        public int seed = 431;
        public Vector3 speciesWeights = new Vector3(78,6,.8f);
        public float sunlight = 1.08f, sunElevation = 60, sunAzimuth = -38, shadowStrength = .58f;
        public Color sunlightColor = new Color(1,.98f,.93f), ambientColor = new Color(.65f,.60f,.52f);
        public bool oilEnabled;
        public float oilStrength = .35f, oilRadius = 2, oilAspect = 1.5f, oilAngle;
        public int oilColorSteps;
        public bool oilCompare;

        public Material[] Materials => new[] { grass[0],grass[1],grass[2],ground,paper,paperFold,magicBook,dictionary,orangeFlowers,letterPaper };

        public void SetMaterials(Material[] values)
        {
            grass = new[] { values[0],values[1],values[2] }; ground = values[3];
            paper = values[4]; paperFold = values[5]; magicBook = values[6]; dictionary = values[7]; orangeFlowers = values[8];
            letterPaper = values[9];
        }
    }
}
