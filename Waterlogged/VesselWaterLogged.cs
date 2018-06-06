using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Waterlogged
{
    public class VesselWaterLogged : VesselModule
    {
        public static Dictionary<Vessel, VesselWaterLogged> VesselModules;

        public event Action<bool> OnColorizerEnabledChanged;

        private bool _colorizerEnabled;
        public bool ColorizerEnabled
        {
            get { return _colorizerEnabled; }
            set
            {
                _colorizerEnabled = value;

                OnColorizerEnabledChanged?.Invoke(value);
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
            ColorizerEnabled = value;
        }
    }
}
