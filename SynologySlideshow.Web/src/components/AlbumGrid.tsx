import React from 'react';
import { Album } from '../types';

interface AlbumGridProps {
  albums: Album[];
  currentAlbumId: number;
  onSelectAlbum: (album: Album) => void;
}

export function AlbumGrid({ albums, currentAlbumId, onSelectAlbum }: AlbumGridProps) {
  return (
    <ul className="album-cards">
      {albums.map((album) => (
        <li
          key={album.id}
          className={album.id === currentAlbumId ? 'selected' : ''}
          style={{ backgroundImage: `url('${album.thumbnail}')` }}
          onClick={() => onSelectAlbum(album)}
        >
          <div className="album-info">
            <span className="title">{album.name}</span>
            {album.id === currentAlbumId && (
              <span className="selected-indicator">
                <span className="indicator-dot"></span>
                Playing
              </span>
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}
