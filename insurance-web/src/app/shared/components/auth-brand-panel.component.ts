import { Component } from '@angular/core';
import { BRAND } from '../../core/brand';
import { BrandLogoComponent } from './brand-logo.component';

// The branded side of the sign-in and register pages.
@Component({
  selector: 'app-auth-brand-panel',
  imports: [BrandLogoComponent],
  templateUrl: "./auth-brand-panel.component.html",
  styleUrls: ['./auth-brand-panel.component.css', '../styles/falling-leaves.css'],
})
export class AuthBrandPanelComponent {
  readonly slogan = BRAND.slogan;

  readonly points = [
    'Transparent quotes: see exactly how your price is built',
    'Pay on your schedule, with a heads-up before anything is due',
    'File and track claims online, with clear updates at every step',
  ];

  // Fixed values (not random) so the leaves look the same on every visit.
  // Negative delays mean the leaves are already falling when the page loads.
  readonly leaves = [
    { left: 5, delay: -1.0, duration: 9.0, size: 16 },
    { left: 13, delay: -6.2, duration: 11.5, size: 12 },
    { left: 21, delay: -3.4, duration: 8.5, size: 18 },
    { left: 30, delay: -8.1, duration: 10.5, size: 14 },
    { left: 38, delay: -2.2, duration: 9.5, size: 20 },
    { left: 47, delay: -5.6, duration: 12.0, size: 13 },
    { left: 55, delay: -0.4, duration: 8.0, size: 17 },
    { left: 63, delay: -7.3, duration: 10.0, size: 15 },
    { left: 71, delay: -4.1, duration: 11.0, size: 19 },
    { left: 79, delay: -1.8, duration: 9.0, size: 12 },
    { left: 87, delay: -6.9, duration: 10.5, size: 16 },
    { left: 94, delay: -3.0, duration: 8.5, size: 14 },
  ].map((l, i) => ({ ...l, color: `var(--leaf-${(i % 4) + 1})`, alt: i % 2 === 1 }));
}
