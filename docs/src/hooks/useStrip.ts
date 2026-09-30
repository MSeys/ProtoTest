import {useEffect, useRef} from 'react';

/*
 * A strip that scrolls sideways - tabs, a view switcher - with no scrollbar of its own. A mouse wheel scrolls
 * it along while it has room, then hands the scroll back to the page; data-more names each side that still
 * has more, for a fade; the selected entry is kept in view. The viewer's v-strip does the same.
 */
export function stripMore(element: HTMLElement): string {
  const max = element.scrollWidth - element.clientWidth;
  if (max <= 1) return '';
  const start = element.scrollLeft > 1;
  const end = element.scrollLeft < max - 1;
  return start && end ? 'both' : start ? 'start' : end ? 'end' : '';
}

export default function useStrip<T extends HTMLElement>(selectedKey?: unknown) {
  const ref = useRef<T>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) return undefined;
    const update = () => {
      const more = stripMore(element);
      if (more) element.dataset.more = more;
      else delete element.dataset.more;
    };
    const wheel = (event: WheelEvent) => {
      if (Math.abs(event.deltaX) >= Math.abs(event.deltaY)) return;
      const max = element.scrollWidth - element.clientWidth;
      if (max <= 0) return;
      const next = Math.max(0, Math.min(max, element.scrollLeft + event.deltaY));
      if (next === element.scrollLeft) return;
      event.preventDefault();
      element.scrollLeft = next;
      update();
    };
    element.dataset.strip = '';
    element.addEventListener('wheel', wheel, {passive: false});
    element.addEventListener('scroll', update, {passive: true});
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update);
    observer?.observe(element);
    update();
    return () => {
      element.removeEventListener('wheel', wheel);
      element.removeEventListener('scroll', update);
      observer?.disconnect();
    };
  }, []);

  // Only the strip moves to show the selected entry; scrolling it into view could also scroll the page.
  useEffect(() => {
    const element = ref.current;
    const active = element?.querySelector<HTMLElement>("[aria-selected='true'], [aria-pressed='true']");
    if (!element || !active) return;
    const left = active.offsetLeft - element.offsetLeft;
    if (left < element.scrollLeft) element.scrollLeft = left;
    else if (left + active.offsetWidth > element.scrollLeft + element.clientWidth)
      element.scrollLeft = left + active.offsetWidth - element.clientWidth;
  }, [selectedKey]);

  return ref;
}
