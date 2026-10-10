import React, {type ReactNode} from 'react';
import OriginalMDXA from '@theme-original/MDXComponents/A';
import type {Props} from '@theme/MDXComponents/A';

export default function MDXA({href, ...props}: Props): ReactNode {
  // Bundled downloads are files, not routes: bypass trailingSlash and client-side navigation.
  const target = href?.startsWith('/assets/files/') ? `pathname://${href}` : href;
  return <OriginalMDXA {...props} href={target} />;
}
