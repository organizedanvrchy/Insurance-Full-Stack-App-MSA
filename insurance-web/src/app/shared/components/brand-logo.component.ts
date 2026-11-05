import { Component, input } from '@angular/core';

// Umbrella mark with two autumn leaves + "KVB Insurance" wordmark. Colors come from CSS variables, so it works in both themes.
// A parent can override --logo-mark, --logo-text and --logo-accent (the sign-in panel does).
@Component({
  selector: 'app-brand-logo',
  template: `
    <span class="logo" [class.large]="size() === 'lg'">
      <svg class="mark" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
        <path class="canopy" d="M4 16C4 9.37 9.37 4 16 4s12 5.37 12 12H4z" />
        <path class="handle" d="M16 16v8.5a3 3 0 0 1-6 0" />
        <path class="leaf leaf-a" d="M19.5 27.5c0-4.4 2.9-7.3 7.3-7.5-.2 4.4-3 7.3-7.3 7.5z" />
        <path class="leaf leaf-b" d="M25.6 31c.3-2.6 2.2-4.2 4.7-4.1-.3 2.5-2.2 4.1-4.7 4.1z" />
      </svg>
      <span class="word"><span class="accent">KVB</span> Insurance</span>
    </span>
  `,
  styles: [
    `
      :host {
        display: inline-flex;
      }
      .logo {
        display: inline-flex;
        align-items: center;
        gap: 0.5rem;
        font-size: 1.15rem;
        font-weight: 800;
        letter-spacing: -0.01em;
        line-height: 1;
      }
      .logo.large {
        font-size: 1.9rem;
        gap: 0.65rem;
      }
      .mark {
        width: 1.7em;
        height: 1.7em;
      }
      .canopy {
        fill: var(--logo-mark, var(--primary));
      }
      .handle {
        fill: none;
        stroke: var(--logo-mark, var(--primary));
        stroke-width: 2.4;
        stroke-linecap: round;
      }
      .leaf-a {
        fill: var(--leaf-1);
      }
      .leaf-b {
        fill: var(--leaf-2);
      }
      .word {
        color: var(--logo-text, var(--text));
      }
      .accent {
        color: var(--logo-accent, var(--accent));
      }
    `,
  ],
})
export class BrandLogoComponent {
  size = input<'md' | 'lg'>('md');
}
