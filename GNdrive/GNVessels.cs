using System.Collections.Generic;
using UnityEngine;

namespace GNTechnology
{
    public class GNVessels : VesselModule
    {
        protected override void OnStart()
        {
            GameEvents.onVesselWasModified.Add(OnVesselModified);
        }

        private void OnVesselModified(Vessel v)
        {
            // debug
            var spheres = vessel.FindPartModulesImplementing<GNTestSphereFX>();
            foreach (var s in spheres)
            {
                Debug.Log("[GN] Found sphere FX: " + s.part.partInfo.title);
                if (s.FieldON) s.IsShipChanged = true;
            }

            // main effect
            var spheresMain = vessel.FindPartModulesImplementing<GNShieldModule>();
            foreach (var s in spheresMain)
            {
                Debug.Log("[GN] Found sphere FX: " + s.part.partInfo.title);
                if (s.FieldON) s.IsShipChanged = true;
            }
        }
    }
}
