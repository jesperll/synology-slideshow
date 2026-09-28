import React, { useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useChannelConnection } from '../hooks/useChannelConnection';
import { forgetChannel, rememberChannel } from '../services/channelMemory';
import { ChannelViewPresentation } from './ChannelViewPresentation';

export function ChannelView() {
  const { channelName } = useParams<{ channelName: string }>();
  const navigate = useNavigate();
  const connection = useChannelConnection(channelName!);

  useEffect(() => {
    if (connection.notFound) {
      forgetChannel();
      navigate('/', { replace: true });
    }
  }, [connection.notFound, navigate]);

  useEffect(() => {
    if (connection.state) {
      rememberChannel(connection.state.name);
    }
  }, [connection.state]);

  if (!connection.state) {
    return null;
  }

  return (
    <ChannelViewPresentation
      state={connection.state}
      onNext={connection.requestNext}
      onPrevious={connection.requestPrevious}
      onTogglePause={connection.requestTogglePause}
      onSwitchAlbum={connection.requestSwitchAlbum}
      onLeave={() => {
        forgetChannel();
        navigate('/', { replace: true });
      }}
    />
  );
}
