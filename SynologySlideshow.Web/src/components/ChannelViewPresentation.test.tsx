import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ChannelViewPresentation } from './ChannelViewPresentation';
import * as api from '../services/api';
import { ChannelState } from '../types';

vi.mock('../services/api');

describe('ChannelViewPresentation', () => {
  const baseState: ChannelState = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false };

  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [] } as any);
    vi.mocked(api.getAlbumSlides).mockResolvedValue({
      data: [{ id: 10, uri: '/img/10.jpg', description: '', location: '', date: '' }]
    } as any);
  });

  it('fetches and renders the slide matching the pushed currentSlideId', async () => {
    render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    await waitFor(() => expect(api.getAlbumSlides).toHaveBeenCalledWith(5));
  });

  it('resolves to no rendered slide if the pushed slide id is not in the album', async () => {
    vi.mocked(api.getAlbumSlides).mockResolvedValue({ data: [] } as any);

    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    await waitFor(() => expect(api.getAlbumSlides).toHaveBeenCalled());
    // NOTE: the brief's original selector was '.full-screen[style]', but that also matches
    // the SwipeArea wrapper div (always className="full-screen" with an inline
    // touchAction/overscrollBehavior style), so it can never be 0 regardless of whether a
    // slide rendered. Narrowed to the style SlideLayer actually sets, which is what this
    // test means to assert: no slide image layer was rendered.
    expect(container.querySelectorAll('[style*="background-image"]').length).toBe(0);
  });

  it('opens the overlay on local double-click without affecting the shared channel', () => {
    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);

    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();
  });

  it('keeps rendering the current slide behind the overlay once it is open', async () => {
    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );
    await waitFor(() => expect(api.getAlbumSlides).toHaveBeenCalled());

    fireEvent.doubleClick(container.querySelector('.scrim')!);

    // Opening settings must not hide, unmount, or otherwise stop the slide showing behind it -
    // the slideshow keeps playing for this viewer and everyone else on the channel.
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();
    expect(container.querySelectorAll('[style*="background-image"]').length).toBeGreaterThan(0);
  });

  it('Space opens the overlay, and a second Space closes it', () => {
    render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.keyDown(window, { code: 'Space' });
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();

    fireEvent.keyDown(window, { code: 'Space' });
    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });

  it('does not show the local overlay when isPaused turns true from a remote push', () => {
    const { rerender } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    rerender(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: true }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });

  it('shows the channel picker instead of a leave button when on the default channel', async () => {
    vi.mocked(api.getChannels).mockResolvedValue({ data: [{ id: 1, name: 'kitchen', isDefault: false }] } as any);
    const { container } = render(
      <MemoryRouter>
        <ChannelViewPresentation
          state={{ ...baseState, isDefault: true }}
          onNext={vi.fn()}
          onPrevious={vi.fn()}
          onSwitchAlbum={vi.fn()}
          onLeave={vi.fn()}
        />
      </MemoryRouter>
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    expect(await screen.findByText('Point this device at a channel')).toBeInTheDocument();
    expect(screen.queryByText('Leave channel')).not.toBeInTheDocument();
  });

  it('shows a leave-channel button instead of the channel picker on a named channel', () => {
    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }));

    expect(screen.getByText('Leave channel')).toBeInTheDocument();
    expect(screen.queryByText('Point this device at a channel')).not.toBeInTheDocument();
  });
});
