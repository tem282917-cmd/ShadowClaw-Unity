# Chapter01 Mesh LOD import policy

This project uses Unity **6000.3.24f1**, with the Built-in Render Pipeline. The policy applies only to FBX files inside the opt-in folders below. Source Blender/GLB files, other chapters, project quality settings, build settings, and the playable main scene are not changed.

| Folder under `Assets/Chapter01/Models` | Highest generated LOD index | Selection bias |
| --- | ---: | ---: |
| `Architecture` | 2 | -1 (castle silhouette kept longer) |
| `Infrastructure` | 4 | -1; C01–C04 renderers use -2 to retain player-path detail longer |
| `Trees` | 3 | 0 |
| `Rocks` | 4 | 0 |
| `Distant` | 4 | 0 |

Unity's automatic **Mesh LOD** generates progressively shorter index buffers at import and retains the original LOD0 geometry. The generator may stop before the configured maximum. Normal MeshRenderers select the level using projected screen size, which accounts for camera distance, object size, field of view and resolution. These are not fixed meter thresholds. The project-wide Mesh LOD Threshold is intentionally left unchanged; camera-specific tuning belongs to the eventual playable Chapter01 scene.

Export mesh-only FBX files with original UVs, normals, materials, object hierarchy and transforms. Preserve shared mesh instances; do not merge a complete castle/landscape into one giant mesh. Models in different categories should be in separate FBX files because the generation limit is a ModelImporter setting. Use `Models/ManualLOD` for hand-authored `_LOD0/_LOD1/...` models and LODGroup assets; this folder is excluded. To opt out an individual categorized FBX, include `chapter01.manual-lod` in its importer `userData` and reimport; the policy then disables Generate Mesh LODs.

## Compatibility

- Unity 6.3 static batching and classic GPU instancing render Mesh LOD assets at LOD0. The policy clears only the `BatchingStatic` flag on imported MeshRenderers and warns if an assigned material enables instancing. Keep material instancing disabled for this path. Do not later call `StaticBatchingUtility.Combine` on these models or mark their instances Batching Static.
- Occlusion flags remain available. An occlusion bake still needs its own scene and camera-path validation.
- The current Built-in pipeline does not provide GPU Resident Drawer Mesh LOD cross-fading. Inspect transitions in the gameplay camera; use manual LODs for any silhouette that fails this check.
- LOD reduces submitted triangles, not material count, draw calls, texture memory, light cost, or fog cost. No FPS improvement is promised without a full scene profile.
- Geometry sample FBXs are unreferenced verification assets; they are not complete material/lighting integration.

## Verification

`Shadow Lab > Chapter01 > Validate actual Mesh LOD samples` checks the three actual source-derived sample FBXs against the Blender export manifest, generated index ranges and renderer eligibility. It renders automatic near/far views, then compares each automatic view pixel-for-pixel with all forced LODs at the same camera. This identifies the actual automatically selected level when there is a unique match. Neutral-material LOD0/coarsest pairs are also saved. Results go to `Design/Chapter01-Production-v03/engine-lod-validation-v001` outside Assets. It does not save or modify a game scene.

Verified with project Mesh LOD Threshold **1**, vertical FOV **35 degrees**, and a **512 by 512** render target:

| Source sample | Near automatic level / triangles | Far automatic level / triangles | Lowest available triangles |
| --- | --- | --- | ---: |
| E01a rock | LOD0 / 473,306 | LOD4 / 43,646 | 43,646 |
| E02a tree | LOD0 / 351,350 | LOD3 / 58,688 | 58,688 |
| E05a distant house | LOD0 / 138,402 | LOD3 / 23,417 | 11,711 |

Each automatic image matched exactly one known level. Near/far distances are recorded in the JSON; the three source models use different scales, so those meter values must not be reused as final-scene thresholds. These are actual single-model selections, not the full scene's visible-triangle or frame-rate measurements.

Batch entry point: `Chapter01.Editor.Chapter01LodValidation.Run`.

`Shadow Lab > Chapter01 > Build separate full-scene LOD review` consumes the completed five-file `chapter01-export.json`, restores Standard materials, maps Blender coordinates from exported object anchors, and saves `Assets/Chapter01/Scenes/Chapter01_Review.unity`. It then closes/reopens that scene and verifies stored material assignments, automatic LOD settings and sky texture. The overview and player screenshots use an isolated scene culling mask and a 4x MSAA render target without changing project quality settings. The report distinguishes unique mesh counts and all-instance triangle totals from actual camera draw counts. The scene is excluded from build settings. Blender procedural shaders and volume fog are represented only approximately in this review.

`pack_standard_materials.py` repacks the actual audited metallic/roughness inputs into Standard data textures. R stores metallic including the source's actual multiplier (0.1 for the relevant Designer_Logic materials); A stores `1 - roughness * source factor`. Source R is retained in the packed G channel but **is not connected as occlusion**, because the source audit does not establish that it contains AO. `_OcclusionMap` stays empty and `_OcclusionStrength=0`. Unconnected metallic inputs retain their original constants. The generated textures import as linear data, with `_GlossMapScale=1` and `_SmoothnessTextureChannel=0`; factors are not applied a second time. The 93 mapped materials share 77 packed textures. The exact source-node audit is `atmosphere-v024/material-source-links.json`.

Infrastructure's raw source contains 260 coincident-position, zero-area triangles that the FBX importer removes: 252 in `Valley_Village_Rock_Cliff` and 2 in each of four C02 platforms. `audited-import-cleanup.json` checks **all 561** infrastructure meshes against the independent Blender audit and Unity mesh dump. The report preserves `lod0CountMatchesSource=false` for this raw-count difference and separately checks `lod0MatchesAuditedImportBaseline`; it does not use a tolerance or rewrite the source count. Another 36 collinear triangles remain in the imported baseline. The other four categories retain exact raw LOD0 counts.

Twenty-six renderer material slots belong to the two shared A07 halfpost meshes with zero triangles. Those empty slots are recorded explicitly; every nonempty mesh still requires a matched material. Original source objects are kept. `Chapter01ReviewDiagnostics.Run` reproduces the read-only per-mesh and default-slot dump.

The infrastructure limit was raised after the all-instance count revealed that the reused C05 supports and C07 foundation blocks dominated geometry. `Chapter01ReviewScene.ReimportInfrastructureAndBuild` explicitly updates only `BridgeStairsTerrain.fbx` when its stored generation limit needs updating; the other four FBX imports use their caches. No collision settings are changed.

Official Unity 6.3 references:

- [Mesh LOD generation](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/lod/mesh-lod/mesh-lod-generator)
- [Runtime quality](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/lod/mesh-lod/mesh-lod-quality)
- [Compatibility and limitations](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/lod/mesh-lod/mesh-lod-introduction)
