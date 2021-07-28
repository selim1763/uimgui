using System.Threading.Tasks;
using ImGuiNET;
using UImGui.Assets;
using UImGui.Platform;
using UImGui.Renderer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

#if HAS_HDRP
#endif

namespace UImGui
{
	// TODO: Check Multithread run.
	public class UImGui : MonoBehaviour
	{
		private Context _context;
		private IRenderer _renderer;
		private IPlatform _platform;
		private CommandBuffer _renderCommandBuffer;

		[SerializeField]
		private Camera _camera = null;

		[SerializeField]
		private RenderImGui _renderFeature = null;

		[SerializeField]
		private RenderType _rendererType = RenderType.Mesh;

		[SerializeField]
		private InputType _platformType = InputType.InputManager;

		[Tooltip("Null value uses default imgui.ini file.")]
		[SerializeField]
		private IniSettingsAsset _iniSettings = null;

		[Header("Configuration")]

		[SerializeField]
		private UIOConfig _initialConfiguration = new UIOConfig
		{
			ImGuiConfig = ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable,

			DoubleClickTime = 0.30f,
			DoubleClickMaxDist = 6.0f,

			DragThreshold = 6.0f,

			KeyRepeatDelay = 0.250f,
			KeyRepeatRate = 0.050f,

			FontGlobalScale = 1.0f,
			FontAllowUserScaling = false,

			DisplayFramebufferScale = Vector2.one,

			MouseDrawCursor = false,
			TextCursorBlink = false,

			ResizeFromEdges = true,
			MoveFromTitleOnly = true,
			ConfigMemoryCompactTimer = 1f,
		};

		[SerializeField]
		private FontAtlasConfigAsset _fontAtlasConfiguration = null;

		[Header("Customization")]
		[SerializeField]
		private ShaderResourcesAsset _shaders = null;

		[SerializeField]
		private StyleAsset _style = null;

		[SerializeField]
		private CursorShapesAsset _cursorShapes = null;

		[SerializeField]
		private bool _doGlobalEvents = true; // Do global/default Layout event too.

		private BoxCollider scrollCollider;

		public BoxCollider ScrollCollider => scrollCollider;

		public CommandBuffer CommandBuffer => _renderCommandBuffer;

		#region Events
		public event System.Action<UImGui> Layout;
		public event System.Action<UImGui> OnInitialize;
		public event System.Action<UImGui> OnDeinitialize;
		#endregion

		public void Reload()
		{
			OnDisable();
			OnEnable();
		}

		public void SetUserData(System.IntPtr userDataPtr)
		{
			_initialConfiguration.UserData = userDataPtr;
			ImGuiIOPtr io = ImGui.GetIO();
			_initialConfiguration.ApplyTo(io);
		}

		private void Awake()
		{
			_context = UImGuiUtility.CreateContext();
		}

		private void OnDestroy()
		{
			UImGuiUtility.DestroyContext(_context);
		}

		private void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			
			void Fail(string reason)
			{
				enabled = false;
				throw new System.Exception($"Failed to start: {reason}.");
			}

			if (_camera == null)
			{
				Fail(nameof(_camera));
			}

			if (_renderFeature == null && RenderUtility.IsUsingURP())
			{
				Fail(nameof(_renderFeature));
			}

			_renderCommandBuffer = RenderUtility.GetCommandBuffer(Constants.UImGuiCommandBuffer);

			if (RenderUtility.IsUsingURP())
			{
#if HAS_URP
				_renderFeature.Camera = _camera;
#endif
				_renderFeature.CommandBuffer = _renderCommandBuffer;
			}
			else if (!RenderUtility.IsUsingHDRP())
			{
				_camera.AddCommandBuffer(CameraEvent.AfterEverything, _renderCommandBuffer);
			}

			UImGuiUtility.SetCurrentContext(_context);

			ImGuiIOPtr io = ImGui.GetIO();

			_initialConfiguration.ApplyTo(io);
			_style?.ApplyTo(ImGui.GetStyle());

			_context.TextureManager.BuildFontAtlas(io, _fontAtlasConfiguration);
			_context.TextureManager.Initialize(io);

			IPlatform platform = PlatformUtility.Create(_platformType, _cursorShapes, _iniSettings);
			SetPlatform(platform, io);
			if (_platform == null)
			{
				Fail(nameof(_platform));
			}

			SetRenderer(RenderUtility.Create(_rendererType, _shaders, _context.TextureManager), io);
			if (_renderer == null)
			{
				Fail(nameof(_renderer));
			}

			if (_doGlobalEvents)
			{
				UImGuiUtility.DoOnInitialize(this);
			}
			OnInitialize?.Invoke(this);
		}

		private void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;

			UImGuiUtility.SetCurrentContext(_context);
			ImGuiIOPtr io = ImGui.GetIO();

			SetRenderer(null, io);
			SetPlatform(null, io);

			UImGuiUtility.SetCurrentContext(null);

			_context.TextureManager.Shutdown();
			_context.TextureManager.DestroyFontAtlas(io);

			if (RenderUtility.IsUsingURP())
			{
				if (_renderFeature != null)
				{
#if HAS_URP
					_renderFeature.Camera = null;
#endif
					_renderFeature.CommandBuffer = null;
				}
			}
			else
			{
				if (_camera != null)
				{
					_camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _renderCommandBuffer);
				}
			}

			if (_renderCommandBuffer != null)
			{
				RenderUtility.ReleaseCommandBuffer(_renderCommandBuffer);
			}

			_renderCommandBuffer = null;

			if (_doGlobalEvents)
			{
				UImGuiUtility.DoOnDeinitialize(this);
			}
			OnDeinitialize?.Invoke(this);
		}

		private void Update()
		{
			if (_camera == null)
			{
				_camera = GetComponent<Camera>();
				if (_camera != null)
				{
					Reload();
				}
				return;
			}
			
			UImGuiUtility.SetCurrentContext(_context);
			ImGuiIOPtr io = ImGui.GetIO();

			Constants.PrepareFrameMarker.Begin(this);
			_context.TextureManager.PrepareFrame(io);
			
			_platform.PrepareFrame(io, _camera.pixelRect);
			ImGui.NewFrame();
			Constants.PrepareFrameMarker.End();

			Constants.LayoutMarker.Begin(this);
			try
			{
				if (_doGlobalEvents)
				{
					UImGuiUtility.DoLayout(this);
				}

				Layout?.Invoke(this);
			}
			finally
			{
				ImGui.Render();
				Constants.LayoutMarker.End();
			}

			Constants.DrawListMarker.Begin(this);
			_renderCommandBuffer.Clear();
			_renderer.RenderDrawLists(_renderCommandBuffer, ImGui.GetDrawData());
			Constants.DrawListMarker.End();
		}

		private void Reset()
		{
			_camera = GetComponent<Camera>();
			_initialConfiguration.SetDefaults();
		}

		private void SetRenderer(IRenderer renderer, ImGuiIOPtr io)
		{
			_renderer?.Shutdown(io);
			_renderer = renderer;
			_renderer?.Initialize(io);
		}

		private void SetPlatform(IPlatform platform, ImGuiIOPtr io)
		{
			_platform?.Shutdown(io);
			_platform = platform;
			_platform?.Initialize(io, _initialConfiguration, "Unity " + _platformType.ToString(), this);
		}

		public void SetScrollCollider(BoxCollider scrollCollider)
		{
			this.scrollCollider = scrollCollider;
		}
		
		public Camera GetRenderCamera()
		{
			return _camera;
		}
		
		private static async void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			Camera camera = FindObjectOfType<UImGui>()._camera;
			await Task.Yield();
			
			camera.gameObject.SetActive(false);
			await Task.Yield();
			await Task.Yield();
			camera.gameObject.SetActive(true);
		}
	}
}