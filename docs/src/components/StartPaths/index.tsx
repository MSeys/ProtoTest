import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import styles from './styles.module.css';

const paths = [
  {title: 'New to integration testing?', text: 'Learn the foundations, then run your first suite.', to: '/learn/', label: 'Follow the Learn track'},
  {title: 'Evaluating ProtoTest?', text: 'Compare the options, measurements and adoption cost.', to: '/docs/project/compare', label: 'Compare ProtoTest'},
  {title: 'Already have a suite?', text: 'Find a recipe or explain a failing run.', to: '/docs/recipes/overview', label: 'Browse recipes'},
];
export default function StartPaths(): ReactNode {
  return (
    <nav className={styles.paths} aria-label="Find your starting point">
      {paths.map((path) => (
        <Link key={path.to} to={path.to} className={styles.path}>
          <strong>{path.title}</strong>
          <span>{path.text}</span>
          <b>{path.label} <span aria-hidden="true">→</span></b>
        </Link>
      ))}
    </nav>
  );
}