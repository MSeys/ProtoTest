import React, {type ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import {useLocation} from '@docusaurus/router';
import Layout from '@theme/Layout';
import BlogSidebar from '@theme/BlogSidebar';
import type {Props} from '@theme/BlogLayout';

import styles from './styles.module.css';

// The blog index and its later pages open with the home's blueprint hero; an article opens with a slim band
// back to the index, so the article's own heading stays the page title.
const indexPath = /^\/blog\/?(page\/\d+\/?)?$/;

function BlogBand(): ReactNode {
  const {pathname} = useLocation();
  if (indexPath.test(pathname)) {
    return (
      <header data-surface="blueprint" className={styles.hero}>
        <div className="container">
          <p className={styles.eyebrow}>The ProtoTest blog</p>
          <h1>
            Integration testing,
            <span>told from the evidence.</span>
          </h1>
          <p className={styles.lead}>
            Articles on the problems behind the framework: tests that cross boundaries, failures you can read, and
            fixes you can prove.
          </p>
        </div>
      </header>
    );
  }

  return (
    <div data-surface="blueprint" className={styles.band}>
      <div className="container">
        <Link to="/blog" className={styles.back}>
          ← All articles
        </Link>
      </div>
    </div>
  );
}

export default function BlogLayout(props: Props): ReactNode {
  const {sidebar, toc, children, ...layoutProps} = props;
  const hasSidebar = sidebar && sidebar.items.length > 0;

  return (
    <Layout {...layoutProps}>
      <BlogBand />
      <div className="container margin-vert--lg">
        <div className="row">
          <BlogSidebar sidebar={sidebar} />
          <main
            className={clsx('col', {
              'col--7': hasSidebar,
              'col--9 col--offset-1': !hasSidebar,
            })}>
            {children}
          </main>
          {toc && <div className="col col--2">{toc}</div>}
        </div>
      </div>
    </Layout>
  );
}
