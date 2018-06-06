using System;
using System.Collections.Generic;

namespace Waterlogged
{
    public class VesselWaterLogged : VesselModule
    {
        public static Dictionary<Vessel, VesselWaterLogged> VesselModules;

        public event Action<bool> OnWaterLoggedEnabledChanged;

        private bool _waterLoggedEnabled;
        public bool WaterLoggedEnabled
        {
            get { return _waterLoggedEnabled; }
            set
            {
                _waterLoggedEnabled = value;

                OnWaterLoggedEnabledChanged?.Invoke(value);
            }
        }

        protected override void OnAwake()
        {
            if (VesselModules == null)
                VesselModules = new Dictionary<Vessel, VesselWaterLogged>();
        }

        protected override void OnStart()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return;

            if (VesselModules.ContainsKey(vessel))
                VesselModules[vessel] = this;
            else
                VesselModules.Add(vessel, this);
        }

        public void ToggleWaterColorizer(bool value)
        {
            WaterLoggedEnabled = value;
        }
    }
}
