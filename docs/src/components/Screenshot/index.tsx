import type {ReactNode} from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';
import ThemedImage from '@theme/ThemedImage';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

interface ScreenshotProps {
  /**
   * The figure under static/images. Without an extension it names a pair, `name-light.png` and `name-dark.png`;
   * with one (`name.webp`) it is a screen that has one theme of its own, such as GitHub's, shown in both.
   */
  name: string;
  /** What the figure shows, for screen readers. */
  alt: string;
  /** One sentence under the figure: what to look at. */
  caption: ReactNode;
  /** The figure's size in CSS pixels (the files are captured at twice that). */
  width: number;
  height: number;
}

/**
 * A crop of a real ProtoTest screen, in the site's light or dark theme, with a ring in the site's accent around what
 * the step is about. The figures are captured from real traces by the studio's capture scripts, so they can be made
 * again for a release. Diagrams that are not screens are components of their own (RunCompare, RaceDiagram).
 */
export default function Screenshot({name, alt, caption, width, height}: ScreenshotProps): ReactNode {
  const single = /\.\w+$/.test(name);
  const light = useBaseUrl(single ? `/images/${name}` : `/images/${name}-light.png`);
  const dark = useBaseUrl(single ? `/images/${name}` : `/images/${name}-dark.png`);
  // The frame is a size container, so it cannot take its width from the image: the figure gives it one.
  return (
    <div className={styles.figure} style={{maxWidth: width + 2}}>
      <Frame kind="preview" foot={caption}>
        <ThemedImage className={styles.image} alt={alt} width={width} height={height} sources={{light, dark}} loading="lazy" />
      </Frame>
    </div>
  );
}
