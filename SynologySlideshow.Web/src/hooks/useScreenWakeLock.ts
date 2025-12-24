import { useEffect, useState } from 'react';

export interface WakeLockSentinel {
  release: () => Promise<void>;
}

export function useScreenWakeLock(isEnabled: boolean = true) {
  const [sentinel, setSentinel] = useState<WakeLockSentinel | null>(null);
  
  const isSupported = 'wakeLock' in navigator;
  
  const requestWakeLock = async (): Promise<WakeLockSentinel | null> => {
    if (!isSupported) return null;

    try {
      const wakeLock = await (navigator as any).wakeLock.request('screen');
      console.log('[WakeLock] ✓ Acquired wake lock successfully');
      setSentinel(wakeLock);
      return wakeLock;
    } catch (err) {
      console.error('[WakeLock] ✗ Failed to request wake lock:', err);
      return null;
    }
  };

  const releaseWakeLock = async () => {
    if (sentinel) {
      console.log('[WakeLock] Releasing wake lock');
      await sentinel.release();
      setSentinel(null);
    }
  };

  useEffect(() => {    
    if (isSupported && isEnabled) {
      requestWakeLock();
    } else if (!isEnabled && sentinel) {
      releaseWakeLock();
    }

    return () => {
      releaseWakeLock();
    };
  }, [isEnabled]);

  // Re-acquire wake lock when document becomes visible again
  useEffect(() => {
    if (!isSupported || !isEnabled) return;

    const handleVisibilityChange = () => {
      if (!document.hidden && !sentinel) {
        requestWakeLock();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);

    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, [isSupported, isEnabled, sentinel]);

  return { isSupported, sentinel, requestWakeLock, releaseWakeLock };
}
