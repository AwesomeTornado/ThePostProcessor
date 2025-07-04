using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.Undo;
using HarmonyLib;
using ResoniteModLoader;
using SkyFrost.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering.PostProcessing;
using UnityFrooxEngineRunner;
using static UnityFrooxEngineRunner.Helper;

namespace ThePostProcessor
{
    public class ThePostProcessor : ResoniteMod
    {
        internal const string VERSION_CONSTANT = "1.3.4.0";
        public override string Name => "ThePostProcessor";
        public override string Author => "NepuShiro, Cloud_Jumper, __Choco__";
        public override string Version => VERSION_CONSTANT;
        public override string Link => "https://github.com/0xFLOATINGPOINT/ThePostProcessor/";
        //# add LICENSE to code?

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<dummy> PERMS_DUMMY = new("perms_dummy", "--------------------- Anti-Aliasing ---------------------");
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<AntiAliasing> antiAliasing = new ModConfigurationKey<AntiAliasing>("MSAA", "MSAA (Multisample Anti-Aliasing) level. Requires Forward Renderpath. MSAA is heavy on GPU/VRAM so you may see a drop in FPS", () => AntiAliasing.None);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<dummy> PERMS_DUMMY2 = new("perms_dummy2", "--------------------- Color Grading ---------------------");
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<Uri> LutURI = new ModConfigurationKey<Uri>("Color Grading Asset URI", "The URI used for loading the Color Grading Asset Asset. typically this is on resdb:///", () => new("resdb:///35c1d6249ee3b063e38c7e3ea4f506fe9bad7265f7505a1f947a80fd9558496a.3dtex")); // local://8mdjnurwnydnshgc44jzas95aukpyqjw3q5wdoxrsi8ys6fw1wzy/UN78XSrG_E6vyhUisVqtrA.3dtex
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> HDRLut = new ModConfigurationKey<bool>("Is Color Grading Asset HDR", "Switch between Linear(HDR)/true and SRGB(SDR)/false", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> toolShelfLut = new ModConfigurationKey<bool>("Allow Color Grading Asset On ToolShelf", "Search the Toolshelf for LUTs (StaticTexture3Ds) and auto apply them as the Color Grading asset", () => false);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<dummy> PERMS_DUMMY3 = new("perms_dummy3", "--------------------- Misc ---------------------");
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("Mod Enabled", "", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> EnableOverlayCamera = new ModConfigurationKey<bool>("EnableOverlayCamera", "", () => false);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> LutEnabled = new ModConfigurationKey<bool>("Color Grading Enabled", "", () => false);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<RenderingPath> renderPath = new ModConfigurationKey<RenderingPath>("Camera RenderPath", "Changes The RenderPath that is used for the affected cameras.", () => RenderingPath.UsePlayerSettings);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> hdr = new ModConfigurationKey<bool>("Camera HDR (Experimental)", "Toggles affected cameras between HDR/SDR. disabling this can lower GPU and VRAM usage, but breaks Bloom and can cause colorbanding.", () => true);
        [AutoRegisterConfigKey]

        private static ModConfigurationKey<bool> Notes = new ModConfigurationKey<bool>("Notes", "All settings here only apply to the POV (Point Of View) camera not to any other in-game cameras", () => false);

        private static ModConfiguration config;

        private static UnityEngine.Camera OverlayCamera;

        public enum AntiAliasing
        {
            None = 0,
            TwoX = 2,
            FourX = 4,
            EightX = 8
        }

        private static void ForAllMainCameras(Action<UnityEngine.Camera> actions)
        {
            foreach (var camera in UnityEngine.Object.FindObjectsOfType<UnityEngine.Camera>())
            {
                if (camera.gameObject.tag == "MainCamera")
                {
                    actions(camera);
                }
            }
        }

        private static void ForOverlayCameras(Action<UnityEngine.Camera> actions)
        {
            if (OverlayCamera != null)
            {
                actions(OverlayCamera);
                return;
            }
            foreach (var camera in UnityEngine.Object.FindObjectsOfType<UnityEngine.Camera>())
            {
                if (camera.gameObject.name == "OverlayCamera")
                {
                    OverlayCamera = camera;
                    actions(camera);
                }
            }
        }

        private static bool ModEnabled
        {
            get
            {
                return config.GetValue(ENABLED);
            }
        }

        private static bool ShouldRemoveLUT
        {
            get
            {
                return !ModEnabled || !config.GetValue(LutEnabled);
            }
        }

        private static Uri internalLutUrl = null;

        public override void OnEngineInit()
        {
            config = GetConfiguration();
            config.Save(true);

            ModConfigurationKey.OnChangedHandler updateEnabled = (enabled) =>
            {
                var renderValue = ModEnabled ? config.GetValue(renderPath) : RenderingPath.UsePlayerSettings;
                var msaaValue = ModEnabled ? (int)config.GetValue(antiAliasing) : (int)AntiAliasing.None;
                var hdrValue = ModEnabled ? config.GetValue(hdr) : true;

                ForAllMainCameras((camera) =>
                {
                    camera.renderingPath = renderValue;
                    if (ModEnabled) Msg($"Camera: {camera.name}, RenderPath: {camera.renderingPath}");
                    camera.allowHDR = hdrValue;
                    if (ModEnabled) Msg($"Camera: {camera.name}, HDR: {camera.allowHDR}");
                });
                QualitySettings.antiAliasing = msaaValue;
                if (ModEnabled) Msg($"Set MSAA Level {msaaValue}x");

                UpdateLut();
            };
            renderPath.OnChanged += (path) =>
            {
                if (ModEnabled is false) return;
                ForAllMainCameras((camera) =>
                {
                    camera.renderingPath = (RenderingPath)path;
                    Msg($"Camera: {camera.name}, RenderPath: {camera.renderingPath}");
                });
            };
            antiAliasing.OnChanged += (msaaValue) =>
            {
                if (ModEnabled is false) return;
                QualitySettings.antiAliasing = (int)msaaValue;
                Msg($"Set MSAA Level {msaaValue}x");
            };
            hdr.OnChanged += (hdrValue) =>
            {
                if (ModEnabled is false) return;
                ForAllMainCameras((camera) =>
                {
                    camera.allowHDR = (bool)hdrValue;
                    Msg($"Camera: {camera.name}, HDR: {camera.allowHDR}");
                });
            };
            LutEnabled.OnChanged += (lutEnabled) =>
            {
                UpdateLut();
            };
            LutURI.OnChanged += (lutUrl) =>
            {
                internalLutUrl = (Uri)lutUrl;
                UpdateLut();
            };
            ModConfigurationKey.OnChangedHandler updateOverylayCamera = (EnableOverlayCamera) =>//TODO: Call this when the user changes the respective setting.
            {
                //TODO: Patch this to handle switching between desktop and VR?
                //may want to find a singular function that only gets called when VR active and dash opened.
                /*
                 * private void InputInterface_VRActiveChanged(bool active)
		            {
			            base.RunSynchronously(new Action(this.UpdateOverlayState), false);
		            }


                		protected override void OnAttach()
		                {
			            base.InputInterface.VRActiveChanged += this.InputInterface_VRActiveChanged;
                 */
                //This event will be reworked to simply invalidate the camera enabled boolean, and then call a seperate function which will handle the 
                //logic of "should the overlay camera be disabled" and "is the disabled state already set"
                ForOverlayCameras((camera) =>
                {
                    //Harmony.HasAnyPatches(harmonyId)
                    camera.enabled = ((bool)EnableOverlayCamera || false) && !(Userspace.UserspaceWorld.LocalUser.OutputDevice == OutputDevice.VR); //TODO: Replace false with "is 3dDashOnScreen" loaded
                    Msg("Camera.enabled set to " + camera.enabled);
                });
            };
            toolShelfLut.OnChanged += (toolShelf) =>
            {
                Slot userRoot = Engine.Current.WorldManager.FocusedWorld.LocalUser.Root.Slot;

                List<AvatarRoot> list = Pool.BorrowList<AvatarRoot>();
                userRoot.GetFirstDirectComponentsInChildren(list);
                Slot avatarRoot = list.FirstOrDefault().Slot;
                Pool.Return(ref list);

                if ((bool)toolShelf)
                {
                    avatarRoot.ForeachComponentInChildren<ItemShelf>((shelf) =>
                    {
                        shelf.ContentSlot.ChildAdded += OnChildAdded;
                        shelf.ContentSlot.ChildRemoved += OnChildRemove;
                    });
                }
                else
                {
                    avatarRoot.ForeachComponentInChildren<ItemShelf>((shelf) =>
                    {
                        shelf.ContentSlot.ChildAdded -= OnChildAdded;
                        shelf.ContentSlot.ChildRemoved -= OnChildRemove;
                    });
                }
            };

            ENABLED.OnChanged += updateEnabled;

            Engine.Current.OnReady += () =>
             {
                 updateEnabled.Invoke(ModEnabled);
             };

            Harmony harmony = new Harmony("com.Cloud_Jumper.ThePostProcessor");
            harmony.PatchAll();
            Msg("ThePostProcessor loaded.");
        }

        [HarmonyPatchCategory(nameof(InitializeLUTOnUserspaceInit))]
        [HarmonyPatch(typeof(Userspace), "BootstrapAsync")]
        private class InitializeLUTOnUserspaceInit
        {
            private static void Postfix()
            {
                Msg("Initializing LUT settings");
                internalLutUrl = config.GetValue(LutURI);
                UpdateLut();
            }
        }



        private static void UpdateLut()
        {
            bool removing = ShouldRemoveLUT;
            Slot userspace = Userspace.UserspaceWorld.RootSlot;
            if (userspace is null) return;
            //gotta run synchronously because we are adding slots/components.
            userspace.RunSynchronously(() =>
            {
                Slot lutSlot;
                StaticTexture3D icon = null;
                if (!removing)
                {
                    lutSlot = userspace.FindChildOrAdd("LUTHolder");
                    lutSlot.PersistentSelf = false;

                    VolumeUnlitMaterial mat = lutSlot.GetComponentOrAttach<VolumeUnlitMaterial>();
                    icon = lutSlot.GetComponentOrAttach<StaticTexture3D>();
                    mat.Volume.Target = icon;
                    icon.URL.Value = internalLutUrl;
                    icon.PreferredProfile.Value = config.GetValue(HDRLut) ? ColorProfile.Linear : ColorProfile.sRGB;
                    icon.Uncompressed.Value = true;
                    icon.DirectLoad.Value = true;
                    icon.Readable.Value = true;
                }

                //TODO: This will run in five frames, so that FrooxEngine has the time to upload icon to Unity
                //This five frame delay can probably be shortened to something like one frame, but it works as is.
                userspace.RunInUpdates(5, () =>
                {
                    ForAllMainCameras((camera) =>
                    {
                        Msg($"Running stuff on {camera.name}");
                        foreach (var pp in camera.GetComponents<PostProcessLayer>())
                        {
                            if (!pp.defaultProfile.TryGetSettings<ColorGrading>(out var apple))
                            {
                                Msg($"PP Colorgrading not found, adding");
                                apple = pp.defaultProfile.AddSettings<ColorGrading>();
                            }

                            apple.gradingMode.overrideState = !removing;
                            apple.externalLut.overrideState = !removing;

                            apple.gradingMode.value = removing ? GradingMode.HighDefinitionRange : GradingMode.External;
                            apple.externalLut.value = removing ? null : icon.RawAsset.GetUnity();//Icon is null when removing is True, therefore this conditional may be redundant.
                        }
                    });
                });
            });
        }

        private static void OnChildAdded(Slot slot, Slot child)
        {
            var texture3d = child.GetComponentInChildren<StaticTexture3D>();
            if (texture3d is null) return; //if we don't find any LUTs, exit.
            Msg($"LUT added from: {child.Name}, on {slot.Name}");
            internalLutUrl = texture3d.URL.Value;
            UpdateLut();
        }

        private static void OnChildRemove(Slot slot, Slot child)
        {
            var texture3d = child.GetComponentInChildren<StaticTexture3D>();
            if (texture3d is not null) return; // if we don't find any LUT's, change back to default URL
            Msg($"LUT Removed from: {slot.Name}");
            internalLutUrl = config.GetValue(LutURI);
            UpdateLut();
        }
    }
}
