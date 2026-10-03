import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ChannelView } from './ChannelView';
import * as connectionHook from '../hooks/useChannelConnection';
import * as memory from '../services/channelMemory';
import * as api from '../services/api';

vi.mock('../hooks/useChannelConnection');
vi.mock('../services/channelMemory');
// ChannelView renders the real ChannelViewPresentation, which fetches albums/slides on
// mount. Mock services/api so these tests don't hit real (unmocked) axios calls.
vi.mock('../services/api');

describe('ChannelView', () => {
  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [] } as any);
    vi.mocked(api.getAlbumSlides).mockResolvedValue({ data: [] } as any);
  });

  it('redirects to / and forgets the channel when the channel is not found', async () => {
    vi.mocked(connectionHook.useChannelConnection).mockReturnValue({
      state: null,
      notFound: true,
      requestNext: vi.fn(),
      requestPrevious: vi.fn(),
      requestJump: vi.fn(),
      requestTogglePause: vi.fn(),
      requestSwitchAlbum: vi.fn()
    });

    render(
      <MemoryRouter initialEntries={['/missing-channel']}>
        <Routes>
          <Route path="/:channelName" element={<ChannelView />} />
          <Route path="/" element={<div>home</div>} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(memory.forgetChannel).toHaveBeenCalled());
  });

  it('remembers the channel name once state arrives', async () => {
    vi.mocked(connectionHook.useChannelConnection).mockReturnValue({
      state: { channelId: 1, name: 'kitchen', currentAlbumId: null, currentSlideId: null, isPaused: true, isDefault: false },
      notFound: false,
      requestNext: vi.fn(),
      requestPrevious: vi.fn(),
      requestJump: vi.fn(),
      requestTogglePause: vi.fn(),
      requestSwitchAlbum: vi.fn()
    });

    render(
      <MemoryRouter initialEntries={['/kitchen']}>
        <Routes>
          <Route path="/:channelName" element={<ChannelView />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => expect(memory.rememberChannel).toHaveBeenCalledWith('kitchen'));
  });
});
