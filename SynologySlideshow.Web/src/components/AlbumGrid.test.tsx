import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AlbumGrid } from './AlbumGrid';

describe('AlbumGrid', () => {
  const albums = [
    { id: 1, name: 'Holiday', thumbnail: '/a.jpg', photoCount: 120 },
    { id: 2, name: 'Birthday', thumbnail: '/b.jpg', photoCount: 7 }
  ];

  it("shows each album's photo count", () => {
    render(<AlbumGrid albums={albums} currentAlbumId={1} onSelectAlbum={vi.fn()} />);

    expect(screen.getByText('120 photos')).toBeInTheDocument();
    expect(screen.getByText('7 photos')).toBeInTheDocument();
  });

  it('selects an album when its card is clicked', () => {
    const onSelectAlbum = vi.fn();
    render(<AlbumGrid albums={albums} currentAlbumId={1} onSelectAlbum={onSelectAlbum} />);

    fireEvent.click(screen.getByText('Birthday'));

    expect(onSelectAlbum).toHaveBeenCalledWith(albums[1]);
  });
});
