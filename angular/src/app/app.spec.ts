import { TestBed, type ComponentFixture } from '@angular/core/testing';

import { App } from './app';

describe('App OnPush demonstration', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [App] }).compileComponents();
  });

  it('creates the demo with matching parent and child counts', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(fixture.componentInstance).toBeTruthy();
    expect(text(fixture, '[data-testid="source-count"]')).toBe('2');
    expect(text(fixture, '[data-testid="rendered-count"]')).toBe('2 visible');
    expect(rows(fixture)).toHaveLength(2);
  });

  it('demonstrates same-reference mutation versus immutable replacement', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    click(fixture, '1. Mutate existing array');

    expect(text(fixture, '[data-testid="source-count"]')).toBe('3');
    expect(text(fixture, '[data-testid="rendered-count"]')).toBe('2 visible');
    expect(rows(fixture)).toHaveLength(2);

    click(fixture, '2. Replace array immutably');

    expect(text(fixture, '[data-testid="source-count"]')).toBe('4');
    expect(text(fixture, '[data-testid="rendered-count"]')).toBe('4 visible');
    expect(rows(fixture)).toHaveLength(4);
  });

  it('updates an OnPush view from a signal write and resets with a fresh reference', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    click(fixture, 'Update signal counter');
    expect(text(fixture, '[data-testid="signal-count"]')).toBe('1');

    click(fixture, '1. Mutate existing array');
    click(fixture, 'Reset demo');

    expect(text(fixture, '[data-testid="source-count"]')).toBe('2');
    expect(text(fixture, '[data-testid="rendered-count"]')).toBe('2 visible');
    expect(text(fixture, '[data-testid="signal-count"]')).toBe('0');
  });
});

function click(fixture: ComponentFixture<App>, name: string): void {
  const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
  const button = Array.from(buttons).find((candidate) => candidate.textContent?.trim() === name);

  expect(button).toBeTruthy();
  button!.click();
  fixture.detectChanges();
}

function text(fixture: ComponentFixture<App>, selector: string): string | undefined {
  return (fixture.nativeElement.querySelector(selector) as HTMLElement | null)?.textContent?.trim();
}

function rows(fixture: ComponentFixture<App>): NodeListOf<HTMLElement> {
  return fixture.nativeElement.querySelectorAll(
    '[data-testid="notification-row"]',
  ) as NodeListOf<HTMLElement>;
}
