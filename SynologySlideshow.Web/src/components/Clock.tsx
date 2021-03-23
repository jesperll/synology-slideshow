import React, { useState, useEffect } from 'react';

const DK_LOCALE = 'da-DK';

export function Clock() {
  const [now, setNow] = useState(new Date());

  useEffect(() => {
    const timer = setInterval(() => {
      setNow(new Date());
    }, 1000);

    return () => clearInterval(timer);
  }, []);

  const timeFormatter = new Intl.DateTimeFormat(DK_LOCALE, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false
  });

  const dateFormatter = new Intl.DateTimeFormat(DK_LOCALE, {
    weekday: 'short',
    day: 'numeric',
    month: 'long'
  });

  return (
    <div className="clock">
      <div className="time">{timeFormatter.format(now)}</div>
      <div className="date">{dateFormatter.format(now)}</div>
    </div>
  );
}
