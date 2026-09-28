import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RootRoute } from './RootRoute';
import * as memory from '../services/channelMemory';

vi.mock('../services/channelMemory');
vi.mock('./Home', () => ({ Home: () => <div>anonymous home</div> }));

describe('RootRoute', () => {
  it('redirects to the remembered channel when one is stored', () => {
    vi.mocked(memory.getRememberedChannel).mockReturnValue('kitchen');

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRoute />} />
          <Route path="/:channelName" element={<div>channel page</div>} />
        </Routes>
      </MemoryRouter>
    );

    expect(screen.getByText('channel page')).toBeInTheDocument();
  });

  it('renders the anonymous Home when nothing is remembered', () => {
    vi.mocked(memory.getRememberedChannel).mockReturnValue(null);

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRoute />} />
        </Routes>
      </MemoryRouter>
    );

    expect(screen.getByText('anonymous home')).toBeInTheDocument();
  });
});
