import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// vitest.config.ts does not set `test.globals: true`, so @testing-library/react's
// automatic afterEach cleanup (which relies on detecting a global `afterEach`) never
// registers. Without this, DOM from one test's render() leaks into the next test in the
// same file. Wire it up explicitly for every test file.
afterEach(() => {
  cleanup();
});

// jsdom doesn't implement scrollIntoView at all; anything that calls it (e.g. scrolling the
// admin jump-to-slide grid to the current slide) throws "not a function" without this.
if (typeof Element.prototype.scrollIntoView !== 'function') {
  Element.prototype.scrollIntoView = () => {};
}

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
  (globalThis as any).localStorage = localStorage;
}
