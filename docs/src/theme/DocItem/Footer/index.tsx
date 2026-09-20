import OriginalFooter from '@theme-original/DocItem/Footer';
import type OriginalFooterType from '@theme/DocItem/Footer';
import type {WrapperProps} from '@docusaurus/types';
import type {ReactNode} from 'react';

import DocFeedback from '@site/src/components/DocFeedback';

type Props = WrapperProps<typeof OriginalFooterType>;

export default function DocItemFooterWrapper(props: Props): ReactNode {
  return (
    <>
      <DocFeedback />
      <OriginalFooter {...props} />
    </>
  );
}
