import { Component, input } from '@angular/core';

// Umbrella mark with two autumn leaves + "KVB Insurance" wordmark. Colors come from CSS variables, so it works in both themes.
// A parent can override --logo-mark, --logo-text and --logo-accent (the sign-in panel does).
@Component({
  selector: 'app-brand-logo',
  template: `
    <span class="logo" [class.large]="size() === 'lg'">
      <img class="mark" src="favicon.png" alt="" aria-hidden="true" />
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
        width: auto;
        height: 3em;
      }
    `,
  ],
})
export class BrandLogoComponent {
  size = input<'md' | 'lg'>('md');
}
