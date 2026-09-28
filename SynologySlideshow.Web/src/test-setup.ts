import '@testing-library/jest-dom/vitest';

// Ensure localStorage is available in test environment
if (typeof localStorage === 'undefined') {
  const localStorageData: Record<string, string> = {};
  const localStorage = {
    getItem: (key: string) => localStorageData[key] || null,
    setItem: (key: string, value: string) => {
      localStorageData[key] = value;
    },
    removeItem: (key: string) => {
      delete localStorageData[key];
    },
    clear: () => {
      Object.keys(localStorageData).forEach(key => {
        delete localStorageData[key];
      });
    },
    length: 0,
    key: (index: number) => Object.keys(localStorageData)[index] || null
  };
  (global as any).localStorage = localStorage;
}
