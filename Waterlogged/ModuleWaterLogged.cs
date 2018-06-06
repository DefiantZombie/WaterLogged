#if DEBUG
#define ENABLE_PROFILER
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
#if DEBUG
using UnityEngine.Profiling;
#endif

namespace Waterlogged
{
    public class ModuleWaterLogged : PartModule
    {
        private MaterialPropertyBlock _mpb;
        private int _colorPropertyId;

        private List<Renderer> _renderers = new List<Renderer>();
        private List<Color> _colors = new List<Color>();

        private Gradient _gradient;

        [KSPField(guiName = "#SSC_BO_000001", advancedTweakable = true,
            guiActive = true, guiActiveEditor = false, isPersistant = false)]
        [UI_Toggle(affectSymCounterparts = UI_Scene.None, scene = UI_Scene.Flight)]
        public bool Toggle = false;

        [KSPField(guiName = "#SSC_BO_000002", advancedTweakable = true,
            guiActive = false, guiActiveEditor = false, isPersistant = true, guiFormat = "P")]
        public double SubmergedAmount = 0;

        [KSPField(guiName = "#SSC_BO_000003", advancedTweakable = true,
            guiActive = false, guiActiveEditor = false, isPersistant = true, guiFormat = "N2")]
        public float RBDrag = 0;

        private bool _enabled = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            _mpb = new MaterialPropertyBlock();
            _gradient = new Gradient();

            _colorPropertyId = Shader.PropertyToID("_Color");

            CreateRendererList();
            SetGradient();

            Fields["Toggle"].OnValueModified += OnToggleModified;
        }

        public override void OnStartFinished(StartState state)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return;

            if (vessel == null) return;

            VesselWaterLogged.VesselModules[vessel].OnColorizerEnabledChanged += OnColorizerEnabledChanged;
        }

        private void CreateRendererList()
        {
            var renderers = transform.GetComponentsInChildren<Renderer>();
            var renderCount = renderers.Length;
            for (var i = 0; i < renderCount; i++)
            {
                var materials = renderers[i].materials;
                var materialCount = materials.Length;
                for (var j = 0; j < materialCount; j++)
                {
                    if (materials[j].HasProperty(_colorPropertyId))
                    {
                        _renderers.Add(renderers[i]);
                        _colors.Add(materials[j].GetColor(_colorPropertyId));
                    }
                }
            }
        }

        private void SetGradient()
        {
            var gck = new GradientColorKey[5];
            var gak = new GradientAlphaKey[5];

            gck[0].color = XKCDColors.TanGreen;
            gck[0].time = 0f;
            gck[1].color = XKCDColors.Green;
            gck[1].time = 0.25f;
            gck[2].color = XKCDColors.Yellow;
            gck[2].time = 0.5f;
            gck[3].color = XKCDColors.Orange;
            gck[3].time = 0.75f;
            gck[4].color = XKCDColors.Red;
            gck[4].time = 1.0f;

            gak[0].alpha = 1.0f;
            gak[0].time = 0f;
            gak[1].alpha = 1.0f;
            gak[1].time = 0.25f;
            gak[2].alpha = 1.0f;
            gak[2].time = 0.5f;
            gak[3].alpha = 1.0f;
            gak[3].time = 0.75f;
            gak[4].alpha = 1.0f;
            gak[4].time = 1f;

            _gradient.SetKeys(gck, gak);
        }

        public override void OnUpdate()
        {
            if (!_enabled) return;

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (MapView.MapIsEnabled)
                {
                    return;
                }

                var submerged = !Mathf.Approximately((float)part.submergedPortion, 0);

                SubmergedAmount = part.submergedPortion;
                if (part.rb != null)
                {
                    RBDrag = part.rb.drag;
                }

#if DEBUG
                Profiler.BeginSample("WaterColorizerUpdate");
#endif
                var count = _renderers.Count;
                while (count-- > 0)
                {
                    if (_renderers[count] == null)
                    {
                        _renderers.RemoveAt(count);
                        _colors.RemoveAt(count);
                        continue;
                    }

                    if (submerged)
                    {
                        var color = _gradient.Evaluate((float)part.submergedPortion);
                        SetColor(_renderers[count], color);
                    }
                    else
                    {
                        ResetColor(_renderers[count], _colors[count]);
                    }
                }
#if DEBUG
                Profiler.EndSample();
#endif
            }
        }

        private void OnToggleModified(object o)
        {
            var value = (bool) o;

            VesselWaterLogged.VesselModules[vessel].ToggleWaterColorizer(value);
        }

        private void OnColorizerEnabledChanged(bool value)
        {
            if (!value)
                ResetColor();

            _enabled = value;
            Toggle = value;

            Fields["SubmergedAmount"].guiActive = value;
            Fields["RBDrag"].guiActive = value;
        }

        private void ResetColor()
        {
            var count = _renderers.Count;
            while (count-- > 0)
            {
                if (_renderers[count] == null)
                {
                    _renderers.RemoveAt(count);
                    _colors.RemoveAt(count);
                    continue;
                }

                ResetColor(_renderers[count], _colors[count]);
            }
        }

        private void ResetColor(Renderer r, Color c)
        {
            _mpb.SetColor(_colorPropertyId, c);
            r.SetPropertyBlock(_mpb);
        }

        private void SetColor(Renderer r, Color c)
        {
            _mpb.SetColor(_colorPropertyId, c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
