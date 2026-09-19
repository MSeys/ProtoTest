import type {ComponentProps, ReactNode} from 'react';
import MDXComponents from '@theme-original/MDXComponents';

/**
 * Every markdown table sits in its own horizontal scroller, so a wide one scrolls inside the column instead of
 * widening the page. The table inside stays a real table and fills the column when it is narrower.
 */
function Table(props: ComponentProps<'table'>): ReactNode {
  return (
    <div className="table-scroll">
      <table {...props} />
    </div>
  );
}

export default {
  ...MDXComponents,
  table: Table,
};
