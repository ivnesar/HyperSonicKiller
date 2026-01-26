using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;

namespace RetroShadersPro.URP
{
    internal class CRTMeshShaderGUI : ShaderGUI
    {
        private MaterialProperty baseColorProp = null;
        private const string baseColorName = "_BaseColor";
        private readonly GUIContent baseColorInfo = new("Base Color", 
            "Albedo color of the object.");

        private MaterialProperty baseTexProp = null;
        private const string baseTexName = "_BaseMap";
        private readonly GUIContent baseTexInfo = new("Base Texture", 
            "Albedo texture of the object.");

        private MaterialProperty distortionStrengthProp = null;
        private const string distortionStrengthName = "_DistortionStrength";
        private readonly GUIContent distortionStrengthInfo = new GUIContent("Distortion Strength", 
            "Strength of the barrel distortion. Values above zero cause CRT screen-like distortion; values below zero bulge outwards");

        private MaterialProperty distortionSmoothingProp = null;
        private const string distortionSmoothingName = "_DistortionSmoothing";
        private readonly GUIContent distortionSmoothingInfo = new("Distortion Smoothing", 
            "Amount of smoothing applied to edges of the distorted screen.");

        private MaterialProperty backgroundColorProp = null;
        private const string backgroundColorName = "_BackgroundColor";
        private readonly GUIContent backgroundColorInfo = new("Background Color", 
            "Color of the area outside of the barrel-distorted 'screen'.");

        private MaterialProperty pixelSizeProp = null;
        private const string pixelSizeName = "_PixelSize";
        private readonly GUIContent pixelSizeInfo = new("Pixel Size",
            "Size of each 'pixel' on the new image, after rescaling the source camera texture.");

        private MaterialProperty forcePointFilteringProp = null;
        private const string forcePointFilteringName = "_POINT_FILTERING";
        private readonly GUIContent forcePointFilteringInfo = new("Force Point Filtering",
            "Should the effect use point filtering when rescaling?");

        private MaterialProperty rgbTexProp = null;
        private const string rgbTexName = "_RGBTex";
        private readonly GUIContent rgbTexInfo = new("RGB Subpixel Texture",
            "Small texture denoting the shape of the red, green, and blue subpixels." +
            "\nFor best results, try and make sure the Pixel Size matches the dimensions of this texture.");

        private MaterialProperty rgbStrengthProp = null;
        private const string rgbStrengthName = "_RGBStrength";
        private readonly GUIContent rgbStrengthInfo = new("RGB Subpixel Strength",
            "How strongly the screen colors get multiplied with the subpixel texture.");

        private MaterialProperty scanlineTexProp = null;
        private const string scanlineTexName = "_ScanlineTex";
        private readonly GUIContent scanlineTexInfo = new("Scanline Texture",
            "Small texture denoting the scanline pattern which scrolls over the screen.");

        private MaterialProperty scanlineStrengthProp = null;
        private const string scanlineStrengthName = "_ScanlineStrength";
        private readonly GUIContent scanlineStrengthInfo = new("Scanline Strength",
            "How strongly the scanline texture is overlaid onto the screen.");

        private MaterialProperty scanlineSizeProp = null;
        private const string scanlineSizeName = "_RGBPixelSize";
        private readonly GUIContent scanlineSizeInfo = new("Scanline/RGB Size",
            "The scanline and RGB textures cover this number of pixels." +
            "\nFor best results, this should be a multiple of the Pixel Size.");

        private MaterialProperty scrollSpeedProp = null;
        private const string scrollSpeedName = "_ScrollSpeed";
        private readonly GUIContent scrollSpeedInfo = new("Scanline Scroll Speed",
            "How quickly the scanlines scroll vertically over the screen.");

        private MaterialProperty randomWearProp = null;
        private const string randomWearName = "_RandomWear";
        private readonly GUIContent randomWearInfo = new("Random Wear",
            "How strongly each texture line is offset horizontally.");

        private MaterialProperty aberrationStrengthProp = null;
        private const string aberrationStrengthName = "_AberrationStrength";
        private readonly GUIContent aberrationStrengthInfo = new("Aberration Strength",
            "Amount of color channel separation at the screen edges.");

        private const string useAberrationName = "_CHROMATIC_ABERRATION_ON";

        private MaterialProperty trackingTextureProp = null;
        private const string trackingTextureName = "_TrackingTex";
        private readonly GUIContent trackingTextureInfo = new("Tracking Texture",
            "A control texture for VHS tracking artifacts." +
            "\nThe red channel of the texture contains the strength of the UV offsets." +
            "\nThe green channel of the texture contains tracking line strength." +
            "\nStrength values are centered around 0.5 (gray), and get stronger the closer you get to 0 or 1.");

        private MaterialProperty trackingSizeProp = null;
        private const string trackingSizeName = "_TrackingSize";
        private readonly GUIContent trackingSizeInfo = new("Tracking Size",
            "How many times the tracking texture is tiled on-screen.");

        private MaterialProperty trackingStrengthProp = null;
        private const string trackingStrengthName = "_TrackingStrength";
        private readonly GUIContent trackingStrengthInfo = new("Tracking Strength",
            "How strongly the tracking texture offsets screen UVs.");

        private MaterialProperty trackingSpeedProp = null;
        private const string trackingSpeedName = "_TrackingSpeed";
        private readonly GUIContent trackingSpeedInfo = new("Tracking Speed",
            "How quickly the tracking texture scrolls across the screen." +
            "\nUse negative values to scroll upwards instead.");

        private MaterialProperty trackingJitterProp = null;
        private string trackingJitterName = "_TrackingJitter";
        private readonly GUIContent trackingJitterInfo = new("Tracking Jitter",
            "How jittery the scrolling movement is.");

        private MaterialProperty trackingColorDamageProp = null;
        private const string trackingColorDamageName = "_TrackingColorDamage";
        private readonly GUIContent trackingColorDamageInfo = new("Tracking Color Damage",
            "How strongly the chrominance of the image is distorted." +
            "\nThe distortion is applied in YIQ color space, to the I and Q channels (chrominance)." +
            "\nA value of 1 distorts colors back to the original chrominance.");

        private MaterialProperty trackingLinesThresholdProp = null;
        private const string trackingLinesThresholdName = "_TrackingLinesThreshold";
        private readonly GUIContent trackingLinesThresholdInfo = new("Tracking Lines Threshold",
            "Higher threshold values mean fewer pixels are registered as tracking lines.");

        private MaterialProperty trackingLinesColorProp = null;
        private const string trackingLinesColorName = "_TrackingLinesColor";
        private readonly GUIContent trackingLinesColorInfo = new("Tracking Lines Color",
            "Color of the tracking lines. The alpha component acts as a global multiplier on strength.");

        private MaterialProperty brightnessProp = null;
        private const string brightnessName = "_Brightness";
        private readonly GUIContent brightnessInfo = new("Brightness", "Global brightness adjustment control. 1 represents no change." +
            "\nThis setting can be increased if other features darken your image too much.");

        private MaterialProperty contrastProp = null;
        private const string contrastName = "_Contrast";
        private readonly GUIContent contrastInfo = new("Contrast",
            "Global contrast modifier. 1 represents no change.");

        private MaterialProperty alphaClipProp = null;
        private const string alphaClipName = "_AlphaClip";
        private readonly GUIContent alphaClipInfo = new("Alpha Clip",
            "Should the shader clip pixels based on alpha using a threshold value?");

        private MaterialProperty alphaClipThresholdProp = null;
        private const string alphaClipThresholdName = "_Cutoff";
        private readonly GUIContent alphaClipThresholdInfo = new("Threshold",
            "The threshold value to use for alpha clipping.");

        private MaterialProperty cullProp;
        private const string cullName = "_Cull";
        private readonly GUIContent cullInfo = new("Render Face",
            "Should Unity render Front, Back, or Both faces of the mesh?");

        private const string surfaceTypeName = "_Surface";
        private readonly GUIContent surfaceTypeInfo = new("Surface Type",
            "Should the object be transparent or opaque?");

        private const string alphaTestName = "_ALPHATEST_ON";

        private static readonly string[] surfaceTypeNames = Enum.GetNames(typeof(SurfaceType));
        private static readonly string[] renderFaceNames = Enum.GetNames(typeof(RenderFace));

        private enum SurfaceType
        {
            Opaque = 0,
            Transparent = 1
        }

        private enum RenderFace
        {
            Front = 2,
            Back = 1,
            Both = 0
        }

        private SurfaceType surfaceType = SurfaceType.Opaque;
        private RenderFace renderFace = RenderFace.Front;

        protected readonly MaterialHeaderScopeList materialScopeList = new MaterialHeaderScopeList(uint.MaxValue);
        protected MaterialEditor materialEditor;
        private bool firstTimeOpen = true;

        private void FindProperties(MaterialProperty[] props)
        {
            baseColorProp = FindProperty(baseColorName, props, true);
            baseTexProp = FindProperty(baseTexName, props, true);
            distortionStrengthProp = FindProperty(distortionStrengthName, props, true);
            distortionSmoothingProp = FindProperty(distortionSmoothingName, props, true);
            backgroundColorProp = FindProperty(backgroundColorName, props, true);
            pixelSizeProp = FindProperty(pixelSizeName, props, true);
            forcePointFilteringProp = FindProperty(forcePointFilteringName, props, true);
            rgbTexProp = FindProperty(rgbTexName, props, true);
            rgbStrengthProp = FindProperty(rgbStrengthName, props, true);
            scanlineTexProp = FindProperty(scanlineTexName, props, true);
            scanlineStrengthProp = FindProperty(scanlineStrengthName, props, true);
            scanlineSizeProp = FindProperty(scanlineSizeName, props, true);
            scrollSpeedProp = FindProperty(scrollSpeedName, props, true);
            randomWearProp = FindProperty(randomWearName, props, true);
            aberrationStrengthProp = FindProperty(aberrationStrengthName, props, true);
            trackingTextureProp = FindProperty(trackingTextureName, props, true);
            trackingSizeProp = FindProperty(trackingSizeName, props, true);
            trackingStrengthProp = FindProperty(trackingStrengthName, props, true);
            trackingSpeedProp = FindProperty(trackingSpeedName, props, true);
            trackingJitterProp = FindProperty(trackingJitterName, props, true);
            trackingColorDamageProp = FindProperty(trackingColorDamageName, props, true);
            trackingLinesThresholdProp = FindProperty(trackingLinesThresholdName, props, true);
            trackingLinesColorProp = FindProperty(trackingLinesColorName, props, true);
            brightnessProp = FindProperty(brightnessName, props, true);
            contrastProp = FindProperty(contrastName, props, true);

            //surfaceTypeProp = FindProperty(kSurfaceTypeProp, props, false);
            cullProp = FindProperty(cullName, props, true);
            alphaClipProp = FindProperty(alphaClipName, props, true);
            alphaClipThresholdProp = FindProperty(alphaClipThresholdName, props, true);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (materialEditor == null)
            {
                throw new ArgumentNullException("No MaterialEditor found (CRTMeshShaderGUI).");
            }

            Material material = materialEditor.target as Material;
            this.materialEditor = materialEditor;

            FindProperties(properties);

            if (firstTimeOpen)
            {
                materialScopeList.RegisterHeaderScope(new GUIContent("Surface Options"), 1u << 0, DrawSurfaceOptions);
                materialScopeList.RegisterHeaderScope(new GUIContent("Basic Properties"), 1u << 1, DrawBasicProperties);
                materialScopeList.RegisterHeaderScope(new GUIContent("Barrel Distortion"), 1u << 2, DrawDistortionProperties);
                materialScopeList.RegisterHeaderScope(new GUIContent("RGB Subpixels & Scanlines"), 1u << 3, DrawRGBScanlineProperties);
                materialScopeList.RegisterHeaderScope(new GUIContent("VHS Artifacts"), 1u << 4, DrawVHSProperties);
                materialScopeList.RegisterHeaderScope(new GUIContent("Color Adjustments"), 1u << 5, DrawColorProperties);
                firstTimeOpen = false;
            }

            materialScopeList.DrawHeaders(materialEditor, material);
            materialEditor.serializedObject.ApplyModifiedProperties();
        }

        private void DrawSurfaceOptions(Material material)
        {
            surfaceType = (SurfaceType)material.GetFloat(surfaceTypeName);
            renderFace = (RenderFace)material.GetFloat(cullName);

            // Display opaque/transparent options.
            bool surfaceTypeChanged = false;
            EditorGUI.BeginChangeCheck();
            {
                surfaceType = (SurfaceType)EditorGUILayout.EnumPopup(surfaceTypeInfo, surfaceType);
            }
            if (EditorGUI.EndChangeCheck())
            {
                surfaceTypeChanged = true;
            }

            // Display culling options.
            EditorGUI.BeginChangeCheck();
            {
                renderFace = (RenderFace)EditorGUILayout.EnumPopup(cullInfo, renderFace);
            }
            if (EditorGUI.EndChangeCheck())
            {
                switch (renderFace)
                {
                    case RenderFace.Both:
                        {
                            material.SetFloat(cullName, 0);
                            break;
                        }
                    case RenderFace.Back:
                        {
                            material.SetFloat(cullName, 1);
                            break;
                        }
                    case RenderFace.Front:
                        {
                            material.SetFloat(cullName, 2);
                            break;
                        }
                }
            }

            // Display alpha clip options.
            EditorGUI.BeginChangeCheck();
            {
                materialEditor.ShaderProperty(alphaClipProp, alphaClipInfo);
            }
            if (EditorGUI.EndChangeCheck())
            {
                surfaceTypeChanged = true;
            }

            bool alphaClip;

            if (surfaceTypeChanged)
            {
                switch (surfaceType)
                {
                    case SurfaceType.Opaque:
                        {
                            material.SetOverrideTag("RenderType", "Opaque");
                            material.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                            material.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                            material.SetFloat("_ZWrite", 1);
                            material.SetFloat(surfaceTypeName, 0);

                            alphaClip = material.GetFloat(alphaClipName) >= 0.5f;
                            if (alphaClip)
                            {
                                material.EnableKeyword(alphaTestName);
                                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                                material.SetOverrideTag("RenderType", "TransparentCutout");
                            }
                            else
                            {
                                material.DisableKeyword(alphaTestName);
                                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                material.SetOverrideTag("RenderType", "Opaque");
                            }


                            break;
                        }
                    case SurfaceType.Transparent:
                        {
                            alphaClip = material.GetFloat(alphaClipName) >= 0.5f;
                            if (alphaClip)
                            {
                                material.EnableKeyword(alphaTestName);
                            }
                            else
                            {
                                material.DisableKeyword(alphaTestName);
                            }
                            material.SetOverrideTag("RenderType", "Transparent");
                            material.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                            material.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            material.SetFloat("_ZWrite", 0);
                            material.SetFloat(surfaceTypeName, 1);

                            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                            break;
                        }
                }
            }

            alphaClip = material.GetFloat(alphaClipName) >= 0.5f;
            if (alphaClip)
            {
                EditorGUI.indentLevel++;
                materialEditor.ShaderProperty(alphaClipThresholdProp, alphaClipThresholdInfo);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawBasicProperties(Material material)
        {
            materialEditor.ShaderProperty(baseColorProp, baseColorInfo);
            materialEditor.ShaderProperty(baseTexProp, baseTexInfo);

            materialEditor.ShaderProperty(pixelSizeProp, pixelSizeInfo);

            if (forcePointFilteringProp != null)
            {
                materialEditor.ShaderProperty(forcePointFilteringProp, forcePointFilteringInfo);
            }
        }

        private void DrawDistortionProperties(Material material)
        {
            materialEditor.ShaderProperty(distortionStrengthProp, distortionStrengthInfo);
            materialEditor.ShaderProperty(distortionSmoothingProp, distortionSmoothingInfo);
            materialEditor.ShaderProperty(backgroundColorProp, backgroundColorInfo);
        }

        private void DrawRGBScanlineProperties(Material material)
        {
            materialEditor.ShaderProperty(rgbTexProp, rgbTexInfo);
            materialEditor.ShaderProperty(rgbStrengthProp, rgbStrengthInfo);
            materialEditor.ShaderProperty(scanlineTexProp, scanlineTexInfo);
            materialEditor.ShaderProperty(scanlineStrengthProp, scanlineStrengthInfo);
            materialEditor.ShaderProperty(scanlineSizeProp, scanlineSizeInfo);
            materialEditor.ShaderProperty(scrollSpeedProp, scrollSpeedInfo);
        }

        private void DrawVHSProperties(Material material)
        {
            materialEditor.ShaderProperty(randomWearProp, randomWearInfo);

            EditorGUI.BeginChangeCheck();
            materialEditor.ShaderProperty(aberrationStrengthProp, aberrationStrengthInfo);
            if (EditorGUI.EndChangeCheck())
            {
                float aberrationStrength = material.GetFloat(aberrationStrengthName);

                if(aberrationStrength > 0.01f)
                {
                    material.EnableKeyword(useAberrationName);
                }
                else
                {
                    material.DisableKeyword(useAberrationName);
                }
            }

            EditorGUI.BeginChangeCheck();
            materialEditor.ShaderProperty(trackingTextureProp, trackingTextureInfo);
            materialEditor.ShaderProperty(trackingSizeProp, trackingSizeInfo);
            materialEditor.ShaderProperty(trackingStrengthProp, trackingStrengthInfo);
            materialEditor.ShaderProperty(trackingSpeedProp, trackingSpeedInfo);
            materialEditor.ShaderProperty(trackingJitterProp, trackingJitterInfo);
            materialEditor.ShaderProperty(trackingColorDamageProp, trackingColorDamageInfo);
            materialEditor.ShaderProperty(trackingLinesThresholdProp, trackingLinesThresholdInfo);
            materialEditor.ShaderProperty(trackingLinesColorProp, trackingLinesColorInfo);
            if (EditorGUI.EndChangeCheck())
            {
                Texture trackingTexture = material.GetTexture(trackingTextureName);
                float trackingStrength = material.GetFloat(trackingStrengthName);
                float trackingColorDamage = material.GetFloat(trackingColorDamageName);
                float trackingLinesThreshold = material.GetFloat(trackingLinesThresholdName);

                if (trackingTexture == null ||
                    (trackingStrength < 0.001f && trackingColorDamage < 0.001f &&
                    trackingLinesThreshold > 0.999f))
                {
                    material.DisableKeyword("_TRACKING_ON");
                }
                else
                {
                    material.EnableKeyword("_TRACKING_ON");
                }
            }
        }
        
        private void DrawColorProperties(Material material)
        {
            materialEditor.ShaderProperty(brightnessProp, brightnessInfo);
            materialEditor.ShaderProperty(contrastProp, contrastInfo);
        }
    }
}
