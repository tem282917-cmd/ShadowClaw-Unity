using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ShiyinBuild {
    public const string MainScene="Assets/Chapter01/Scenes/Chapter01_Main.unity";
    const string Output="../Builds/Windows-Chapter01-Combat/ShadowClaw.exe";
    [MenuItem("Shadow Lab/Build Shiyin Windows")]
    public static void BuildWindows() {
        foreach(var id in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/ShadowClaw/Resources/Shiyin/Icons"})) {
            var path=AssetDatabase.GUIDToAssetPath(id);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.filterMode=FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        foreach(var line in ClawChecks.Run())Debug.Log(line);
        var report=BuildPipeline.BuildPlayer(new[]{MainScene},Output,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Shiyin Windows build failed: "+report.summary.result);
        Debug.Log("SHIYIN_BUILD_PASS "+report.summary.outputPath);
    }
}
