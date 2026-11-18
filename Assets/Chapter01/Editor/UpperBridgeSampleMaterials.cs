using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chapter01.Editor
{
    /// <summary>Private materials for the upper-bridge sample. UV0 is measured in metres.</summary>
    public static class UpperBridgeSampleMaterials
    {
        public const string Folder = "Assets/Chapter01/UpperBridgeSample/Materials";
        const string PrivateTextures = "Assets/Chapter01/UpperBridgeSample/Textures";
        const string Textures = "Assets/Chapter01/Textures/";
        const string SlateColor = Textures + "Route_Slate_BaseColor_8433d81051.png";
        const string SlateNormal = Textures + "Route_Slate_Normal_e83253e7ec.png";
        // Already converted for Unity Standard: R = metallic, A = smoothness, linear import.
        // The source ORM texture cannot be assigned directly to Standard's metallic slot.
        const string SlateMetallic = Textures + "StandardPacked/Standard_a467cca264692fe4c802.png";
        const string StoneColor = Textures + "rock-basecolor-2048_png_3603998dd2.png";
        const string StoneNormal = Textures + "rock-normal-2048_png_77accc8ba0.png";

        /// <returns>Fixed submesh order: deck, stone, trim, iron, metalAccent.</returns>
        public static Material[] Build()
        {
            EnsureFolder(Folder);
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("The upper bridge requires the built-in Standard shader.");
            var slateColor = Texture(SlateColor);
            var slateNormal = Texture(SlateNormal, true);
            var slateMetallic = Texture(SlateMetallic);
            var stoneColor = Texture(StoneColor);
            var stoneNormal = Texture(StoneNormal, true);
            var metallicImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(slateMetallic));
            if (metallicImporter.sRGBTexture)
                throw new InvalidOperationException("The existing Standard metallic/smoothness texture must be imported as linear data.");

            // Eight-by-ten small stones across a four-metre repeat: approximately 50 x 40 cm.
            // This is an existing tileable surface, not the original model's UV atlas.
            var deck = Make("UB_01_WornSlatePaving", shader, new Color(1.10f, 1.11f, 1.09f), 0, .20f);
            Surface(deck, slateColor, slateNormal, 4f, .42f);
            deck.SetTexture("_MetallicGlossMap", slateMetallic);
            deck.SetTextureScale("_MetallicGlossMap", Vector2.one / 4f);
            deck.SetFloat("_GlossMapScale", .78f);
            deck.EnableKeyword("_METALLICGLOSSMAP");
            Detail(deck, stoneNormal, .7f, .12f);

            // Quiet mineral variation keeps side walls readable under blue moonlight.
            // The geometry supplies stone courses, joints and the arch profile.
            var stone = Make("UB_02_WeatheredSideStone", shader, new Color(.56f, .60f, .63f), 0, .12f);
            Surface(stone, stoneColor, stoneNormal, 1.5f, .34f);
            var trim = Make("UB_03_WornLimestoneEdges", shader, new Color(1.02f, 1.01f, .96f), 0, .19f);
            Surface(trim, stoneColor, stoneNormal, 1.2f, .20f);

            // Restrained specular highlights describe the rail profile without looking polished.
            var iron = Make("UB_04_BlackenedWroughtIron", shader, new Color(.115f, .13f, .145f), .76f, .34f);
            Surface(iron, null, stoneNormal, .35f, .055f);
            var metalAccent = Make("UB_05_AgedBrassFittings", shader, new Color(.42f, .32f, .16f), .72f, .31f);
            Surface(metalAccent, null, stoneNormal, .30f, .065f);

            var materials = new[] { deck, stone, trim, iron, metalAccent };
            foreach (var material in materials) EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log("UPPER_BRIDGE_MATERIALS_READY: 5 Standard materials; metre UVs; 5 private copies of existing tileable maps with anisotropic filtering; no source materials/import settings changed.");
            return materials;
        }

        static Material Make(string name, Shader shader, Color tint, float metallic, float smoothness)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.enableInstancing = false; // Preserve the project's native mesh-LOD path.
            material.doubleSidedGI = false;
            material.renderQueue = -1;
            material.SetOverrideTag("RenderType", "Opaque");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            foreach (string slot in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap",
                "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask", "_ParallaxMap", "_EmissionMap" })
            {
                material.SetTexture(slot, null);
                material.SetTextureScale(slot, Vector2.one);
                material.SetTextureOffset(slot, Vector2.zero);
            }
            material.SetColor("_Color", tint);
            material.SetColor("_EmissionColor", Color.black);
            material.SetFloat("_Mode", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_GlossMapScale", 1);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            material.SetFloat("_OcclusionStrength", 0);
            material.SetFloat("_SpecularHighlights", 1);
            material.SetFloat("_GlossyReflections", 1);
            material.SetFloat("_UVSec", 0);
            return material;
        }

        static void Surface(Material material, Texture2D color, Texture2D normal, float repeatMetres, float relief)
        {
            material.SetTexture("_MainTex", color);
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_MainTex", Vector2.one / repeatMetres);
            material.SetTextureScale("_BumpMap", Vector2.one / repeatMetres);
            material.SetFloat("_BumpScale", relief);
            if (normal != null) material.EnableKeyword("_NORMALMAP");
        }

        static void Detail(Material material, Texture2D normal, float repeatMetres, float strength)
        {
            material.SetTexture("_DetailNormalMap", normal);
            // Standard shares the detail transform with its neutral default grey albedo.
            material.SetTextureScale("_DetailAlbedoMap", Vector2.one / repeatMetres);
            material.SetTextureScale("_DetailNormalMap", Vector2.one / repeatMetres);
            material.SetFloat("_DetailNormalMapScale", strength);
            material.EnableKeyword("_DETAIL_MULX2");
        }

        static Texture2D Texture(string path, bool normal = false)
        {
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var sourceImporter = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!original || sourceImporter == null) throw new InvalidOperationException("Missing existing bridge surface texture: " + path);
            if (normal && sourceImporter.textureType != TextureImporterType.NormalMap)
                throw new InvalidOperationException("Bridge normal texture is not imported as a normal map: " + path);
            if (original.wrapModeU != TextureWrapMode.Repeat || original.wrapModeV != TextureWrapMode.Repeat)
                throw new InvalidOperationException("Bridge texture must already support repeat wrapping: " + path);
            // Ground is seen at a grazing angle in first person. Private imports allow good
            // filtering without changing the original scene's textures or import settings.
            EnsureFolder(PrivateTextures);
            string copyPath = PrivateTextures + "/" + System.IO.Path.GetFileName(path);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath) == null && !AssetDatabase.CopyAsset(path, copyPath))
                throw new InvalidOperationException("Could not create private bridge texture: " + copyPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(copyPath);
            if (importer.anisoLevel != 8 || importer.filterMode != FilterMode.Trilinear || !importer.mipmapEnabled)
            {
                importer.anisoLevel = 8;
                importer.filterMode = FilterMode.Trilinear;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
