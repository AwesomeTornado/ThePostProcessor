using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.Undo;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
        private static ModConfigurationKey<AntiAliasing> antiAliasing = new ModConfigurationKey<AntiAliasing>("MSAA", "The Level that MSAA (Multisample Anti-Aliasing) will be applyed at. Also due to how MSAA works it only works with Forward Renderpath. Note that MSAA is very heavy on the GPU/VRAM so you may see a drop in FPS", () => AntiAliasing.None);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<dummy> PERMS_DUMMY2 = new("perms_dummy2", "--------------------- Color Grading ---------------------");
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> useURLlut = new ModConfigurationKey<bool>("Use Color Grading Asset URI", "Uses Color Grading Asset URI as the target for Color Grading", () => false);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<Uri> LutURI = new ModConfigurationKey<Uri>("Color Grading Asset URI", "The URI used for loading the Color Grading Asset Asset. typically this is on resdb:///", () => new("resdb:///35c1d6249ee3b063e38c7e3ea4f506fe9bad7265f7505a1f947a80fd9558496a.3dtex"));
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> HDRLut = new ModConfigurationKey<bool>("Is Color Grading Asset HDR", "Switch between Linear(HDR)/true and SRGB(SDR)/false", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> toolShelfLut = new ModConfigurationKey<bool>("Allow Color Grading Asset On ToolShelf", "Allows a Color Grading Asset (Note this typically is a imported image as LUT but can be any slot that holds a StaticTexture3D) on your ToolShelves to be used as the target for the Color Grading", () => false);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<dummy> PERMS_DUMMY3 = new("perms_dummy3", "--------------------- Misc ---------------------");
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("Mod Enabled", "", () => true);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> LutEnabled = new ModConfigurationKey<bool>("Color Grading Enabled", "", () => false);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<RenderingPath> renderPath = new ModConfigurationKey<RenderingPath>("Camera RenderPath", "Changes The RenderPath that is used for the affected cameras.", () => RenderingPath.UsePlayerSettings);
        [AutoRegisterConfigKey]
        private static ModConfigurationKey<bool> hdr = new ModConfigurationKey<bool>("Camera HDR (Experimental)", "This changes if the affected cameras internally render in HDR or SDR. disabling this can save a lot on GPU utilization and VRAM, but at the cost of mostly breaking Bloom and can introduce a very small amount of colorbanding depending on lighting.", () => true);
        [AutoRegisterConfigKey]

        private static ModConfigurationKey<bool> Notes = new ModConfigurationKey<bool>("Notes", "All settings here only apply to the POV (Point Of View) camera not to any other in-game cameras", () => false);

        private static ModConfiguration config;

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

        private static bool ModEnabled
        {
            get
            {
                return config.GetValue(ENABLED);
            }
        }

        public override void OnEngineInit()
        {
            config = GetConfiguration();
            config.Save(true);

            ENABLED.OnChanged += (enabled) =>
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

            LutEnabled.OnChanged += (LutEnabled) =>
            {
                if ((bool)LutEnabled) return;
                UpdateLut(null, true);
            };

            ModConfigurationKey.OnChangedHandler updateLuts = (input) =>
            {
                if (config.GetValue(LutEnabled) && config.GetValue(useURLlut))
                {
                    UpdateLut();
                }
                if (config.GetValue(LutEnabled) && !config.GetValue(useURLlut))
                {
                    UpdateLut(null, true);
                }
            };

            useURLlut.OnChanged += updateLuts;
            LutURI.OnChanged += updateLuts;

            toolShelfLut.OnChanged += (toolShelf) =>
            {
                Slot userRoot = Engine.Current.WorldManager.FocusedWorld.LocalUser.Root?.Slot;
                if (userRoot is null) return;

                List<AvatarRoot> list = Pool.BorrowList<AvatarRoot>();
                userRoot.GetFirstDirectComponentsInChildren(list);
                Slot avatarRoot = list.FirstOrDefault()?.Slot;
                Pool.Return(ref list);

                List<Slot> contentSlots = new();
                avatarRoot.ForeachComponentInChildren<ItemShelf>((shelf) =>
                {
                    contentSlots.Add(shelf.ContentSlot);
                });

                if ((bool)toolShelf)
                {
                    foreach (var slot in contentSlots)
                    {
                        slot.ChildAdded += OnChildAdded;
                        slot.ChildRemoved += OnChildRemove;
                    }
                }
                else
                {
                    foreach (var slot in contentSlots)
                    {
                        slot.ChildAdded -= OnChildAdded;
                        slot.ChildRemoved -= OnChildRemove;
                    }
                }
            };

            Engine.Current.OnReady += () =>
            {
                UpdateLut(null, true);
            };

        }

        private static async void UpdateLut(StaticTexture3D icon = null, bool removing = false)
        {
            if (config.GetValue(LutEnabled) is not true) return;

            Slot userspace = Userspace.UserspaceWorld.RootSlot;
            if (userspace is null) return;

            try
            {
                if (!removing)
                {
                    var lutSlot = userspace.FindChildOrAdd("LUTHolder");
                    lutSlot.PersistentSelf = false;

                    if (icon == null)
                    {
                        var mat = lutSlot.GetComponentOrAttach<VolumeUnlitMaterial>();
                        icon = lutSlot.GetComponentOrAttach<StaticTexture3D>();
                        mat.Volume.Target = icon;
                        icon.URL.Value = config.GetValue(LutURI);
                        icon.PreferredProfile.Value = null;
                        icon.PreferredProfile.Value = config.GetValue(HDRLut) ? ColorProfile.Linear : ColorProfile.sRGB;
                        icon.Uncompressed.Value = true;
                        icon.DirectLoad.Value = true;
                        icon.Readable.Value = true;
                    }
                    else if (icon != null)
                    {
                        icon.PreferredProfile.Value = null;
                        icon.PreferredProfile.Value = config.GetValue(HDRLut) ? ColorProfile.Linear : ColorProfile.sRGB;
                        icon.Uncompressed.Value = true;
                        icon.DirectLoad.Value = true;
                        icon.Readable.Value = true;
                    }

                    using var cts = new CancellationTokenSource();
                    await Task.Run(() =>
                    {
                        var now = DateTime.Now;
                        do
                        {
                            if (DateTime.Now - now >= TimeSpan.FromSeconds(10))
                            {
                                Msg("Timeout reached");
                                cts.Cancel();
                                return;
                            }
                        }
                        while (icon.RawAsset == null || icon.RawAsset.Format == Elements.Assets.TextureFormat.Unknown);
                    }, cts.Token);

                    if (cts.IsCancellationRequested) return;
                }
                ForAllMainCameras((camera) =>
                {
                    Msg($"Running stuff on {camera.name}");
                    foreach (var pp in camera.GetComponents<PostProcessLayer>())
                    {
                        if (!pp.defaultProfile.TryGetSettings<ColorGrading>(out var apple))
                        {
                            Msg($"PP Colorgrading not found adding");
                            apple = pp.defaultProfile.AddSettings<ColorGrading>();
                        }

                        apple.gradingMode.overrideState = !removing;
                        apple.externalLut.overrideState = !removing;

                        if (!removing)
                        {
                            apple.gradingMode.value = GradingMode.External;
                            apple.externalLut.value = icon.RawAsset.GetUnity();
                        }
                        else
                        {
                            apple.gradingMode.value = GradingMode.HighDefinitionRange;
                            apple.externalLut.value = null;
                        }

                        Msg(apple.gradingMode.value);
                        Msg(apple.gradingMode.overrideState);
                        Msg(apple.externalLut.value);
                        Msg(apple.externalLut.overrideState);
                    }
                });
            }
            catch (Exception e)
            {
                Msg(e);//is this try catch needed? What is throwing errors? - Choco
                Msg("Exception thrown in UpdateLut");
            }
        }

        private static void OnChildAdded(Slot slot, Slot child)
        {
            try
            {
                var texture3d = child.GetComponentInChildren<StaticTexture3D>();
                if (texture3d != null)
                {
                    Msg($"LUT added from: {child.Name}, on {slot.Name}");
                    UpdateLut(texture3d);
                }
            }
            catch (Exception e)
            {
                Msg(e);
                Msg("Exception thrown in OnChildAdded");
            }
        }

        private static void OnChildRemove(Slot slot, Slot child)
        {
            try
            {
                var texture3d = child.GetComponentInChildren<StaticTexture3D>();
                if (texture3d != null)
                {
                    Msg($"LUT Removed from: {slot.Name}");
                    UpdateLut(null, true);
                }
            }
            catch (Exception e)
            {
                Msg(e);
                Msg("Exception thrown in OnChildRemove");
            }
        }
    }
}
