# Boss room lamp and mist assets

The imported model and effects are existing authored assets. No custom decorative shader or generated texture was created for this change.

## Ornate lantern

- Asset: **Lantern Chandelier 01**, by **Kirill Sannikov**, distributed by Poly Haven.
- Original: https://polyhaven.com/a/lantern_chandelier_01
- License: **CC0 1.0**, https://polyhaven.com/license
- API manifest: https://api.polyhaven.com/files/lantern_chandelier_01
- Imported original FBX and 2K diffuse/normal/glass textures. The Unity prefab scales the existing model to a 4 metre body and uses standard Unity brass/glass materials. Its central flame reuses the game's already imported Nyrthos fire animation.
- The six models retain their three paired bays at world Z = 99 / 117 / 136 and move inward to X = +/-10.2, with their bottoms at Y = 70.5, to clear the original column row. Their wicks and shooting targets follow the replacement chambers. The original six lights retain their nested-prefab hierarchy and use explicit GameplayLight bindings to align with the new wicks.
- Standard brass uses the original diffuse texture for a low emission fill so the ornament stays legible in blackout; glass retains warm/violet state emission. Both materials disable global-illumination contribution. These presentation cues create no additional Light and never enable gameplay shadows.

## Falling mist

- Asset: **Atmospheric GPU Fog Particles**, by **Mirza Beig**.
- Original: https://github.com/MirzaBeig/GPU-Fog-Particles
- License: **Unlicense**; complete upstream license retained in `Mirza-GPU-Fog-LICENSE.txt`.
- Imported unchanged Built-In shader, Fog Small/Large materials and Falling/Ground Fog prefabs. No URP shader or demo code is imported.
- The gate adapts the original Falling Fog prefab's emission box, downward movement and lifetime to the measured 7.6 m wide, 13.2 m high doorway at `(0, 68, 83.475)`, facing inward along +Z. It does not synthesize fog textures or replace the source shader.
- Its physical barrier is initially disabled, closes once the player has fully crossed the entrance plane, and releases on completion. Victory also fades the mist over 1.2 seconds using unscaled time; restarting restores the open mist curtain.

Run `python tools/import_hall_presentation_assets.py` from the repository root to restore the original files. Source URLs, revision and hashes are recorded in `Builds/Hall-Presentation-20261118/source-manifest.json`.
