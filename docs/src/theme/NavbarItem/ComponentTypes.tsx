import ComponentTypes from '@theme-original/NavbarItem/ComponentTypes';

import GitHubLink from '@site/src/components/GitHubLink';
import NuGetLink from '@site/src/components/NuGetLink';

/**
 * Adds the external-link marks: `type: 'custom-github'` and `type: 'custom-nuget'`. Everything else keeps the
 * stock theme's components.
 */
export default {
  ...ComponentTypes,
  'custom-github': GitHubLink,
  'custom-nuget': NuGetLink,
};
