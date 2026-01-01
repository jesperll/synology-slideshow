import { useEffect, useRef, useState } from 'react';

const VERSION_CHECK_INTERVAL = 5 * 60 * 1000; // Check every 5 minutes

export function useVersionCheck() {
  const currentVersionRef = useRef<string | null>(null);
  const isInitializedRef = useRef(false);
  const [updateAvailable, setUpdateAvailable] = useState(false);

  useEffect(() => {
    const checkVersion = async () => {
      try {
        // Add cache-busting query param to avoid cached responses
        const response = await fetch(`/version.json?t=${Date.now()}`, {
          cache: 'no-cache'
        });
        
        if (!response.ok) {
          return;
        }

        const data = await response.json();
        const newVersion = data.hash;

        if (!isInitializedRef.current) {
          // First load - just store the version
          currentVersionRef.current = newVersion;
          isInitializedRef.current = true;
        } else if (currentVersionRef.current && currentVersionRef.current !== newVersion) {
          // Version changed - show update indicator
          setUpdateAvailable(true);
        }
      } catch (error) {
        console.warn('Failed to check version:', error);
      }
    };

    // Check immediately on mount
    checkVersion();

    // Then check periodically
    const interval = setInterval(checkVersion, VERSION_CHECK_INTERVAL);

    return () => clearInterval(interval);
  }, []);

  const reload = () => {
    window.location.reload();
  };

  const dismiss = () => {
    setUpdateAvailable(false);
  };

  return { updateAvailable, reload, dismiss };
}
