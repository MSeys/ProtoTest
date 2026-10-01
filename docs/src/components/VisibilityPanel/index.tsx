import type {ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import ViewerMock from '@site/src/components/ViewerMock';

/*
 * The run screen's header and what the run could see, as the viewer draws them for the demo trace.
 */
export default function VisibilityPanel(): ReactNode {
  return (
    <Frame kind="preview" foot={<>The run screen of the same trace. Absent sources keep their place, drawn dashed.</>}>
      <ViewerMock screen="visibility" />
    </Frame>
  );
}
