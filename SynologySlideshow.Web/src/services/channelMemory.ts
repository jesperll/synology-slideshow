const STORAGE_KEY = 'synology-slideshow-channel-name';

export function rememberChannel(name: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, name);
  } catch {
    // localStorage unavailable (private browsing, blocked storage) — nothing to remember
  }
}

export function forgetChannel(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // localStorage unavailable — nothing to forget
  }
}

export function getRememberedChannel(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
