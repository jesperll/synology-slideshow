import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import { RootRoute } from './RootRoute';
import * as memory from '../services/channelMemory';

function ChannelPageStub() {
  const { channelName } = useParams<{ channelName: string }>();
  return <div>channel page: {channelName}</div>;
}

vi.mock('../services/channelMemory');

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

  it('redirects to the default channel when nothing is remembered', () => {
    vi.mocked(memory.getRememberedChannel).mockReturnValue(null);

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRoute />} />
          <Route path="/:channelName" element={<ChannelPageStub />} />
        </Routes>
      </MemoryRouter>
    );

    expect(screen.getByText('channel page: Default')).toBeInTheDocument();
  });
});
