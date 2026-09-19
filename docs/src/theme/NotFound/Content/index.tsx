import type {ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Heading from '@theme/Heading';
import ThemedImage from '@theme/ThemedImage';
import useBaseUrl from '@docusaurus/useBaseUrl';

import styles from './styles.module.css';

interface Props {
  className?: string;
}

const destinations = [
  {to: '/docs', label: 'Introduction', note: 'What ProtoTest is and where to start'},
  {to: '/docs/getting-started/first-test', label: 'Your first test', note: 'A small suite, one step at a time'},
  {to: '/docs/integrations/overview', label: 'Integrations', note: 'Every capability you can compose'},
  {to: '/search', label: 'Search', note: 'Find the page by what it covers'},
];

/**
 * The 404 page in the site's own voice: the brand mark, what happened, and the four places a reader most
 * likely meant - rather than asking them to write to the site's owner.
 */
export default function NotFoundContent({className}: Props): ReactNode {
  return (
    <main className={clsx('container', styles.page, className)}>
      <ThemedImage
        className={styles.mark}
        alt=""
        sources={{
          light: useBaseUrl('/img/brand/prototest-mark.svg'),
          dark: useBaseUrl('/img/brand/prototest-mark-white.svg'),
        }}
      />
      <Heading as="h1" className={styles.title}>
        This page does not exist.
      </Heading>
      <p className={styles.lead}>
        The address may be from an older version of these docs, or the page moved while the docs are being
        written. One of these is probably what you were after.
      </p>
      <nav className={styles.links} aria-label="Where to go instead">
        {destinations.map((destination) => (
          <Link key={destination.to} className={styles.link} to={destination.to}>
            <strong>{destination.label}</strong>
            <span>{destination.note}</span>
          </Link>
        ))}
      </nav>
    </main>
  );
}
