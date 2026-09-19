import type {PrismTheme} from 'prism-react-renderer';

/**
 * Code blocks sit on the sunken code surface in both colour modes — the same surface the home page panels,
 * the ProtoTrace viewer and the HTML report use. Every colour here is a CSS variable from
 * design/prototest-tokens.css, so this one theme serves light and dark and cannot drift from the tokens.
 */
const prototestPrism: PrismTheme = {
  plain: {
    color: 'var(--code-text)',
    backgroundColor: 'var(--surface-sunken)',
  },
  styles: [
    {types: ['comment', 'prolog', 'doctype', 'cdata'], style: {color: 'var(--code-comment)', fontStyle: 'italic'}},
    {types: ['keyword', 'builtin', 'important', 'atrule'], style: {color: 'var(--code-keyword)'}},
    {types: ['class-name', 'maybe-class-name', 'type-definition'], style: {color: 'var(--code-type)'}},
    {types: ['namespace'], style: {color: 'var(--code-namespace)'}},
    {types: ['function', 'method'], style: {color: 'var(--code-function)'}},
    {types: ['string', 'char', 'regex', 'url', 'inserted'], style: {color: 'var(--code-string)'}},
    {types: ['number', 'boolean', 'constant', 'symbol'], style: {color: 'var(--code-number)'}},
    {types: ['attr-name', 'annotation', 'attribute', 'property', 'tag'], style: {color: 'var(--code-attribute)'}},
    {types: ['punctuation', 'operator'], style: {color: 'var(--code-punctuation)'}},
    {types: ['deleted'], style: {color: 'var(--pt-red)'}},
    {types: ['variable', 'parameter'], style: {color: 'var(--code-text)'}},
  ],
};

export default prototestPrism;
