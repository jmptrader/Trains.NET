﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Comet;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using Trains.NET.Engine;
using Trains.NET.Instrumentation;
using Trains.NET.Rendering;
using Trains.NET.Rendering.Skia;

namespace Trains.NET.Comet
{
    public class MainPage : View
    {
        private readonly State<bool> _configurationShown = false;

        private readonly ILayout _trackLayout;
        private readonly IGameStorage _gameStorage;
        private readonly ITerrainMap _terrainMap;
        private readonly IGame _game;
        private readonly TrainsDelegate _controlDelegate;
        private readonly IInteractionManager _interactionManager;
        private bool _presenting = true;

        public MainPage(IGame game,
                        IEnumerable<ITool> tools,
                        IEnumerable<ILayerRenderer> layers,
                        IEnumerable<ICommand> commands,
                        ITrainController trainControls,
                        ILayout trackLayout,
                        IGameStorage gameStorage,
                        ITerrainMap terrainMap,
                        TrainsDelegate trainsDelegate,
                        IInteractionManager interactionManager)
        {
            this.Title("Trains - " + ThisAssembly.AssemblyInformationalVersion);

            _game = game;
            _controlDelegate = trainsDelegate;
            _interactionManager = interactionManager;
            this.Body = () =>
            {
                return new HStack()
                {
                    new VStack()
                    {
                        _configurationShown ? null :
                            new Button(trainControls.BuildMode ? "Building" : "Playing", () => SwitchGameMode()),
                        new Spacer(),
                        _configurationShown ?
                                CreateConfigurationControls(layers) :
                                CreateToolsControls(tools, trainControls.BuildMode.Value),
                        new Spacer(),
                        _configurationShown || !trainControls.BuildMode ? null :
                            CreateCommandControls(commands),
                        new Spacer(),
                        new HStack()
                        {
                            new Button(" - ", () => _game.ZoomOut())
                                .Frame(40),
                            new Spacer(),
                            new Button(" + ", () => _game.ZoomIn())
                                .Frame(40),
                        },
                        new Spacer(),
                        new Button("Snapshot", () => Snapshot()),
                        new Button("Configuration", ()=> _configurationShown.Value = !_configurationShown.Value)
                    }.Frame(100, alignment: Alignment.Top),
                    new VStack()
                    {
                        new TrainControllerPanel(trainControls),
                        new DrawableControl(_controlDelegate)
                    }
                }.FillHorizontal();
            };

            _trackLayout = trackLayout;
            _gameStorage = gameStorage;

            _ = PresentLoop();

            void SwitchGameMode()
            {
                trainControls.ToggleBuildMode();

                if (_interactionManager == null) return;

                _interactionManager.CurrentTool = tools.FirstOrDefault(t => ShouldShowTool(trainControls.BuildMode, t));
            }

            void Snapshot()
            {
                (int width, int height) = _game.GetSize();
                using var bitmap = new SKBitmap(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                using var skCanvas = new SKCanvas(bitmap);
                using (ICanvas canvas = new SKCanvasWrapper(skCanvas))
                {
                    canvas.Save();
                    _game.Render(canvas);
                    canvas.Restore();
                }
                Clipboard.SetImage(bitmap.ToWriteableBitmap());
            }
            _terrainMap = terrainMap;
        }

        private readonly PerSecondTimedStat _fps = InstrumentationBag.Add<PerSecondTimedStat>("Real-FPS");
        private readonly ElapsedMillisecondsTimedStat _drawTime = InstrumentationBag.Add<ElapsedMillisecondsTimedStat>("Real Draw Time");

        private async Task PresentLoop()
        {
            while (_presenting)
            {
                _drawTime.Start();

                _controlDelegate.Invalidate();

                _drawTime.Stop();

                _fps.Update();

                await Task.Delay(16).ConfigureAwait(true);
            }
        }

        public void Save()
        {
            _gameStorage.WriteStaticEntities(_trackLayout);
            _gameStorage.WriteTerrain(_terrainMap);
        }

        public void Redraw()
        {
            ViewPropertyChanged(ResetPropertyString, null);
        }

        private static View CreateCommandControls(IEnumerable<ICommand> commands)
        {
            var controlsGroup = new VStack();
            foreach (ICommand cmd in commands)
            {
                controlsGroup.Add(new Button(cmd.Name, () => cmd.Execute()));
            }

            return controlsGroup;
        }

        private View CreateToolsControls(IEnumerable<ITool> tools, bool buildMode)
        {
            var controlsGroup = new RadioGroup(Orientation.Vertical);
            foreach (ITool? tool in tools.Where(t => ShouldShowTool(buildMode, t)))
            {
                if (_interactionManager.CurrentTool == null)
                {
                    _interactionManager.CurrentTool = tool;
                }

                controlsGroup.Add(new RadioButton(() => tool.Name, () => _interactionManager.CurrentTool == tool, () => _interactionManager.CurrentTool = tool));
            }

            return controlsGroup;
        }

        private static bool ShouldShowTool(bool buildMode, ITool tool)
            => (buildMode, tool.Mode) switch
            {
                (true, ToolMode.Build) => true,
                (false, ToolMode.Play) => true,
                (_, ToolMode.All) => true,
                _ => false
            };

        private static View CreateConfigurationControls(IEnumerable<ILayerRenderer> layers)
        {
            var layersGroup = new VStack();
            foreach (ILayerRenderer layer in layers)
            {
                layersGroup.Add(new ToggleButton(layer.Name, layer.Enabled, () => layer.Enabled = !layer.Enabled));
            }
            return layersGroup;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _presenting = false;
                _game.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
