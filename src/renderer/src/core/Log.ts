/**
 * Console logging with a level gate. Kept deliberately thin: a screensaver has
 * no user to read logs, so anything beyond "help the developer" is dead weight.
 */
export type LogLevel = 'debug' | 'info' | 'warn' | 'error' | 'silent';

const ORDER: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3, silent: 4 };

let current: LogLevel = import.meta.env.DEV ? 'debug' : 'warn';

export function setLogLevel(level: LogLevel): void {
  current = level;
}

function enabled(level: LogLevel): boolean {
  return ORDER[level] >= ORDER[current];
}

export const log = {
  debug(...args: unknown[]): void {
    if (enabled('debug')) console.debug('[flowscreen]', ...args);
  },
  info(...args: unknown[]): void {
    if (enabled('info')) console.info('[flowscreen]', ...args);
  },
  warn(...args: unknown[]): void {
    if (enabled('warn')) console.warn('[flowscreen]', ...args);
  },
  error(...args: unknown[]): void {
    if (enabled('error')) console.error('[flowscreen]', ...args);
  },
};
