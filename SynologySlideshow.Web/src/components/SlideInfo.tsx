import React from 'react';
import { Slide } from '../types';

const DK_LOCALE = 'da-DK';

interface SlideInfoProps {
  slide: Slide;
}

export function SlideInfo({ slide }: SlideInfoProps) {
  const dateFormatter = new Intl.DateTimeFormat(DK_LOCALE, {    
    day: 'numeric',
    month: 'long',
    year: 'numeric'
  });

  const slideDate = new Date(slide.date);

  return (
    <div className="slide-info">
      <div>{slide.description}</div>
      <div>{slide.location}</div>
      <div>{dateFormatter.format(slideDate)}</div>
    </div>
  );
}
