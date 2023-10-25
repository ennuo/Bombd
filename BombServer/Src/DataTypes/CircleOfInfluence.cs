using System.Collections.Generic;

namespace BombServerEmu_MNR.Src.DataTypes
{
    class CircleOfInfluence
    {
        public enum Type
        {
            Single,
            Series
        }

        public class Event
        {
            public string Name = string.Empty;
            public int Id;
            public int Laps;
            public string Description;
        }
        
        public class Theme
        {
            public Type Type;
            public string Name = string.Empty;
            public string Url = "http://www.modnation.com/";
            public int Index;
            public List<Event> Events = new List<Event>();
        }
    }
}