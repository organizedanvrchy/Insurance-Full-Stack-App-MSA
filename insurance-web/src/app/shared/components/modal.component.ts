import { Component, ElementRef, effect, input, model, viewChild } from '@angular/core';

let nextId = 0;

/**
 * A pop-up on the browser's own <dialog> element: it traps focus, Esc closes it, and the page behind can't be used.
 * Closing it any way (the × button, Esc, clicking outside, or the content setting `open` to false) cancels whatever
 * was in progress. Use with [(open)]: <app-modal title="Change password" [(open)]="showIt">...</app-modal>
 */
@Component({
  selector: 'app-modal',
  template: `
    <dialog #dialog class="modal" [attr.aria-labelledby]="titleId" (close)="open.set(false)" (click)="onBackdrop($event)">
      <div class="modal-card" [style.max-width]="width()">
        <header class="modal-head">
          <h2 [id]="titleId">{{ title() }}</h2>
          <button type="button" class="icon-btn modal-close" aria-label="Close" (click)="open.set(false)">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </header>
        <ng-content />
      </div>
    </dialog>
  `,
  styles: `
    .modal {
      padding: 0;
      border: 0;
      background: transparent;
      width: calc(100% - 32px);
      max-width: none;
      max-height: calc(100dvh - 32px);
    }
    .modal::backdrop {
      background: color-mix(in srgb, #000 45%, transparent);
    }
    .modal-card {
      margin: 0 auto;
      background: var(--surface);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-card);
      padding: 1.25rem 1.5rem 1.5rem;
      display: grid;
      /* minmax(0, …) lets wide content shrink to fit instead of spilling past the card */
      grid-template-columns: minmax(0, 1fr);
      gap: 1rem;
      max-height: calc(100dvh - 32px);
      overflow-y: auto;
    }
    .modal-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }
    .modal-head h2 {
      margin: 0;
      font-size: 1.2rem;
    }
    .modal-close svg {
      width: 18px;
      height: 18px;
    }
  `,
})
export class ModalComponent {
  readonly titleId = `modal-title-${++nextId}`;

  title = input.required<string>();
  width = input('480px');
  open = model(false);

  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const d = this.dialog().nativeElement;
      if (this.open() && !d.open) d.showModal();
      else if (!this.open() && d.open) d.close();
    });
  }

  // A click on the dimmed area outside the card lands on the <dialog> itself.
  onBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) this.open.set(false);
  }
}
