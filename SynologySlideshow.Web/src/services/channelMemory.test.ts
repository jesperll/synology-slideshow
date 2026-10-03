import { beforeEach, describe, expect, it } from 'vitest';
import { rememberChannel, forgetChannel, getRememberedChannel } from './channelMemory';

describe('channelMemory', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('returns null when nothing has been remembered', () => {
    expect(getRememberedChannel()).toBeNull();
  });

  it('returns the remembered channel name after rememberChannel', () => {
    rememberChannel('kitchen');
    expect(getRememberedChannel()).toBe('kitchen');
  });

  it('returns null again after forgetChannel', () => {
    rememberChannel('kitchen');
    forgetChannel();
    expect(getRememberedChannel()).toBeNull();
  });
});
