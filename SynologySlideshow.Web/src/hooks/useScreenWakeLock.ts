import { useEffect, useState } from 'react';

export interface WakeLockSentinel {
  release: () => Promise<void>;
}

export function useScreenWakeLock() {
  const [isSupported, setIsSupported] = useState(false);
  const [sentinel, setSentinel] = useState<WakeLockSentinel | null>(null);

  useEffect(() => {
    setIsSupported('wakeLock' in navigator);
  }, []);

  const requestWakeLock = async (): Promise<WakeLockSentinel | null> => {
    if (!isSupported) return null;

    try {
      const wakeLock = await (navigator as any).wakeLock.request('screen');
      setSentinel(wakeLock);
      return wakeLock;
    } catch (err) {
      console.error('Failed to request wake lock:', err);
      return null;
    }
  };

  const releaseWakeLock = async () => {
    if (sentinel) {
      await sentinel.release();
      setSentinel(null);
    }
  };

  useEffect(() => {
    if (isSupported) {
      requestWakeLock();
    }

    return () => {
      releaseWakeLock();
    };
  }, [isSupported]);

  return { isSupported, sentinel, requestWakeLock, releaseWakeLock };
}
