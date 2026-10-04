import {useEffect, useRef, useState, type ReactNode} from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

const INTRO_ID = 'mFhMaDeQtm8';

interface ClipProps {
  /** The clip's name under static/video: `name.mp4` and its poster `name.jpg`. */
  name: string;
  /** What the clip shows, for screen readers and as the frame's caption. */
  label: string;
  head?: ReactNode;
  className?: string;
}

/**
 * A short silent loop from the intro film. It plays only once the page is in the browser and the reader has not
 * asked for reduced motion; otherwise it shows its poster with the player's controls.
 */
export function Clip({name, label, head, className}: ClipProps): ReactNode {
  const video = useRef<HTMLVideoElement>(null);
  const [still, setStill] = useState(false);
  const src = useBaseUrl(`/video/${name}.mp4`);
  const poster = useBaseUrl(`/video/${name}.jpg`);

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      setStill(true);
      return;
    }
    const element = video.current;
    if (!element) return;
    // Browsers allow a muted video to play on its own; set it before asking. Only a refusal shows the controls,
    // not an interrupted load.
    element.muted = true;
    element.play().catch((error: unknown) => {
      if (error instanceof DOMException && error.name === 'NotAllowedError') setStill(true);
    });
  }, []);

  return (
    <Frame kind="preview" className={className} head={head} foot={label}>
      <video
        ref={video}
        className={styles.video}
        src={src}
        poster={poster}
        aria-label={label}
        muted
        loop
        playsInline
        preload="metadata"
        controls={still}
      />
    </Frame>
  );
}

/**
 * The 70-second introduction. The page shows its thumbnail; the YouTube player, and anything it sets, loads only
 * when the reader presses play, from youtube-nocookie.com.
 */
export function IntroVideo({className}: {className?: string}): ReactNode {
  const [playing, setPlaying] = useState(false);
  const poster = useBaseUrl('/video/intro.jpg');
  return (
    <div className={`${styles.intro} ${className ?? ''}`}>
      {playing ? (
        <iframe
          className={styles.player}
          src={`https://www.youtube-nocookie.com/embed/${INTRO_ID}?autoplay=1&rel=0`}
          title="ProtoTest in 70 seconds"
          allow="autoplay; encrypted-media; picture-in-picture; fullscreen"
          allowFullScreen
        />
      ) : (
        <button type="button" className={styles.cover} onClick={() => setPlaying(true)} aria-label="Play ProtoTest in 70 seconds">
          <img src={poster} alt="" width={1280} height={720} loading="lazy" />
          <span className={styles.play} aria-hidden="true" />
        </button>
      )}
      <p className={styles.note}>
        The player loads from YouTube when you press play.{' '}
        <a href={`https://youtu.be/${INTRO_ID}`}>Watch on YouTube ↗</a>
      </p>
    </div>
  );
}
