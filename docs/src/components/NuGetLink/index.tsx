import type {ReactNode} from 'react';

import NavbarMarkLink from '@site/src/components/NavbarMarkLink';

interface NuGetLinkProps {
  /** Set by the mobile drawer, which lists every navbar item. */
  mobile?: boolean;
  /** Set by the mobile drawer to close itself after a tap. */
  onClick?: () => void;
}

/** The NuGet mark (simple-icons, CC0), pointing at the packages that start with ProtoTest. */
export default function NuGetLink({mobile, onClick}: NuGetLinkProps): ReactNode {
  return (
    <NavbarMarkLink
      href="https://www.nuget.org/packages?q=ProtoTest"
      name="NuGet"
      title="ProtoTest on NuGet"
      mobile={mobile}
      onClick={onClick}>
      <Mark />
    </NavbarMarkLink>
  );
}

function Mark(): ReactNode {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" fill="currentColor">
      <path d="M1.998.342a1.997 1.997 0 1 0 0 3.995 1.997 1.997 0 0 0 0-3.995zm9.18 4.34a6.156 6.156 0 0 0-6.153 6.155v6.667c0 3.4 2.756 6.154 6.154 6.154h6.667c3.4 0 6.154-2.755 6.154-6.154v-6.667a6.154 6.154 0 0 0-6.154-6.155zm-1.477 2.8a2.496 2.496 0 1 1 0 4.993 2.496 2.496 0 0 1 0-4.993zm7.968 6.16a3.996 3.996 0 1 1-.002 7.992 3.996 3.996 0 0 1 .002-7.992z" />
    </svg>
  );
}
