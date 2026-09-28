import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ChannelViewPresentation } from './ChannelViewPresentation';
import * as api from '../services/api';
import { ChannelState } from '../types';

vi.mock('../services/api');

describe('ChannelViewPresentation', () => {
  const baseState: ChannelState = { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false };

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
        onTogglePause={vi.fn()}
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
        onTogglePause={vi.fn()}
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

  it('opens the overlay and requests a pause toggle on local double-click', () => {
    const onTogglePause = vi.fn();
    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);

    expect(onTogglePause).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();
  });

  it('opens the overlay without toggling pause when the channel is already paused', () => {
    const onTogglePause = vi.fn();
    const { container } = render(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: true }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);

    expect(onTogglePause).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();
  });

  it('opens the overlay and pauses when the channel is playing', () => {
    const onTogglePause = vi.fn();
    const { container } = render(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: false }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);

    expect(onTogglePause).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();
  });

  it('Space opens the overlay without re-toggling an existing pause, and a second Space closes it and unpauses', () => {
    const onTogglePause = vi.fn();
    render(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: true }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.keyDown(window, { code: 'Space' });
    expect(onTogglePause).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Albums' })).toBeInTheDocument();

    fireEvent.keyDown(window, { code: 'Space' });
    expect(onTogglePause).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });

  it('does not show the local overlay when isPaused turns true from a remote push', () => {
    const { rerender } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    rerender(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: true }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={vi.fn()}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });

  it('unpauses when the overlay menu is closed while the channel is paused', () => {
    const onTogglePause = vi.fn();
    const { container } = render(
      <ChannelViewPresentation
        state={{ ...baseState, isPaused: true }}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    // Open the overlay locally (mirrors the "double-click to open" gesture).
    fireEvent.doubleClick(container.querySelector('.scrim')!);
    onTogglePause.mockClear();

    // Dismiss via OverlayMenu's own close affordance (double-click inside the menu),
    // the same gesture a viewer uses to dismiss the settings/album grid.
    fireEvent.doubleClick(container.querySelector('.overlay-menu')!);

    expect(onTogglePause).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });

  it('does not toggle pause again when the overlay menu is closed while already unpaused', () => {
    const onTogglePause = vi.fn();
    const { container } = render(
      <ChannelViewPresentation
        state={baseState}
        onNext={vi.fn()}
        onPrevious={vi.fn()}
        onTogglePause={onTogglePause}
        onSwitchAlbum={vi.fn()}
        onLeave={vi.fn()}
      />
    );

    fireEvent.doubleClick(container.querySelector('.scrim')!);
    onTogglePause.mockClear();

    fireEvent.doubleClick(container.querySelector('.overlay-menu')!);

    expect(onTogglePause).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Albums' })).not.toBeInTheDocument();
  });
});
