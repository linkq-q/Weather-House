using UnityEngine;

namespace WeatherHouse.Life
{
    [CreateAssetMenu(menuName="Weather House/Life plant species")]
    public sealed class LifePlantSpecies : ScriptableObject
    {
        public string speciesId,displayName;
        public Mesh[] variants;
        public bool Matches(Mesh mesh)
        {
            if(!mesh || variants==null)return false;
            foreach(Mesh variant in variants)if(variant==mesh)return true;
            return false;
        }
    }
}
