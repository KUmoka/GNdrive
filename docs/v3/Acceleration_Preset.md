# Acceleration preset while stopped

The flight PAW Acceleration setting is a persistent requested target, not the current available acceleration. Shutdown and resource shortage no longer overwrite it with the running-drive cap. Changes to installed capability or synchronization clamp the saved request to the new configuration cap.

The slider upper bound is the sum of installed propulsion-capable drive MaxGContribution multiplied by actual Sync, including stopped or depleted drives. There is no 50 G minimum and an old larger request does not expand the range. Trans-AM does not expand the slider: its multiplier applies to the effective target. A zero synchronization coefficient can produce a zero configuration cap.

Actual effective acceleration remains min(request, sum of running MaxGContribution * Sync), followed by the existing Trans-AM multiplier. Effective accel target displays that effective target; achievable acceleration still depends on power and resources.

Regression checks cover stopped-drive configuration, restarting with the same target, and lower actual capability retaining a larger requested target. Previously saved targets already overwritten as zero cannot be reconstructed; set the desired value once after updating. Flight PAW interaction requires in-game confirmation.
