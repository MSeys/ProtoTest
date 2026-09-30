import Link from '@docusaurus/Link';
import type {ReactNode} from 'react';
import styles from './styles.module.css';
const recipes = [
  {
    label: 'An API call publishes an event',
    to: '/docs/recipes/api-publishes-an-event',
    note: 'REST writes, broker delivers',
  },
  {
    label: 'A write lands in the database',
    to: '/docs/recipes/write-lands-in-the-database',
    note: 'REST writes, SQL stores',
  },
  {
    label: 'Created through the API, shown in the browser',
    to: '/docs/recipes/api-then-browser',
    note: 'REST arranges, browser checks',
  },
  {
    label: 'A downloaded report matches its model',
    to: '/docs/recipes/download-a-report',
    note: 'REST downloads, Sheets reads',
  },
  {
    label: 'Written over REST, read over GraphQL',
    to: '/docs/recipes/rest-then-graphql',
    note: 'REST writes, GraphQL reads',
  },
];
export default function RecipeIndex(): ReactNode {
  return (
    <nav className={styles.recipes} aria-label="Recipes by scenario">
      {recipes.map((recipe) => (
        <Link key={recipe.to} to={recipe.to}>
          <strong>{recipe.label} →</strong>
          <span>{recipe.note}</span>
        </Link>
      ))}
    </nav>
  );
}
