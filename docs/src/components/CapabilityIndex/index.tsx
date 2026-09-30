import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import catalog from '@site/src/data/integrations.json';
import styles from './styles.module.css';
export default function CapabilityIndex({group}: {group: keyof typeof catalog}): ReactNode {
  return (
    <ul className={styles.catalog}>
      {catalog[group].map((item) => (
        <li key={item.name}>
          <div className={styles.title}>
            <strong>{item.name}</strong>
            <span>{item.packages.length === 0 ? 'Built in' : item.preview ? 'Preview' : 'Supported'}</span>
          </div>
          <p>{item.does}</p>
          {item.packages.length > 0 && (
            <div className={styles.packages}>
              {item.packages.map((name) => (
                <code key={name}>{name}</code>
              ))}
            </div>
          )}
          <nav className={styles.links} aria-label={`${item.name} documentation`}>
            {item.links.map((link) => (
              <Link key={link.to} to={link.to}>
                {link.label} →
              </Link>
            ))}
          </nav>
        </li>
      ))}
    </ul>
  );
}
