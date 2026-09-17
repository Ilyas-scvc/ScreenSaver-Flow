import './style.css';
import { FlowScreenApp, WebGL2UnavailableError } from './app/FlowScreenApp';
import { HostBridge, readPayload } from './core/HostBridge';
import { log, setLogLevel } from './core/Log';

function showFatal(container: HTMLElement, title: string, detail: string): void {
  const box = document.createElement('div');
  box.className = 'fs-fatal';

  const h = document.createElement('h1');
  h.textContent = title;

  const p = document.createElement('p');
  p.textContent = detail;

  box.append(h, p);
  container.appendChild(box);
}

/**
 * Reports user activity to the Windows host.
 *
 * WebView2 consumes input before it reaches the host window, so the page has to
 * forward it. The host applies the movement threshold; the page only measures
 * how far the pointer has travelled from where it started, because a screensaver
 * that quits on a one-pixel jitter is a bug users notice immediately.
 */
function wireInputForwarding(bridge: HostBridge): void {
  if (!bridge.isHosted) return;

  let origin: { x: number; y: number } | null = null;

  window.addEventListener(
    'pointermove',
    (event) => {
      if (!origin) {
        origin = { x: event.screenX, y: event.screenY };
        return;
      }
      const dx = event.screenX - origin.x;
      const dy = event.screenY - origin.y;
      bridge.post({ type: 'input', kind: 'mouse-move', distance: Math.hypot(dx, dy) });
    },
    { passive: true },
  );

  window.addEventListener(
    'pointerdown',
    () => bridge.post({ type: 'input', kind: 'mouse-button', distance: 0 }),
    { passive: true },
  );

  window.addEventListener(
    'keydown',
    () => bridge.post({ type: 'input', kind: 'key', distance: 0 }),
    { passive: true },
  );

  window.addEventListener(
    'wheel',
    () => bridge.post({ type: 'input', kind: 'mouse-button', distance: 0 }),
    { passive: true },
  );
}

function boot(): void {
  const container = document.getElementById('app');
  if (!container) throw new Error('#app container is missing from index.html');

  const bridge = new HostBridge();
  const payload = readPayload(container.clientWidth || window.innerWidth, container.clientHeight || window.innerHeight);

  if (!import.meta.env.DEV) {
    setLogLevel(payload.settings.showDebugOverlay ? 'info' : 'warn');
  }

  let app: FlowScreenApp;
  try {
    app = new FlowScreenApp(container, payload, bridge);
  } catch (error) {
    const isWebGl = error instanceof WebGL2UnavailableError;
    const detail = error instanceof Error ? error.message : String(error);
    log.error('failed to start', error);
    bridge.post({ type: 'error', message: detail });
    showFatal(
      container,
      isWebGl ? 'GPU not supported' : 'FlowScreen could not start',
      isWebGl
        ? 'FlowScreen needs WebGL2. Update your graphics driver, or enable hardware acceleration.'
        : detail,
    );
    return;
  }

  bridge.onSettings((settings) => app.applySettings(settings));
  wireInputForwarding(bridge);

  // Development convenience: D toggles the statistics panel.
  window.addEventListener('keydown', (event) => {
    if (!bridge.isHosted && (event.key === 'd' || event.key === 'D')) app.toggleOverlay();
  });

  // Nothing to draw while hidden, and a background tab still burns GPU on the
  // simulation pass if we let it.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) app.stop();
    else app.start();
  });

  app.start();
  bridge.post({ type: 'ready', monitorIndex: payload.view.monitorIndex });
}

boot();
