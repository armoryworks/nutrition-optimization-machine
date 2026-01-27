import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwInlineLoadingComponent,
} from 'angular-material-wrap';

import { ShoppingService } from '../../services/shopping.service';
import { ShoppingListResponseModel } from '../../models/shopping.model';
import { IShoppingListItemModel } from '../../models/shopping-list-item.model.interface';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-shopping-list-export',
  standalone: true,
  imports: [
    AmwCardComponent,
    AmwButtonComponent,
    AmwInlineLoadingComponent,
  ],
  templateUrl: './shopping-list-export.component.html',
  styleUrl: './shopping-list-export.component.scss',
})
export class ShoppingListExportComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private shoppingService = inject(ShoppingService);
  private notificationService = inject(NotificationService);

  // Signals
  listId = signal<number>(0);
  list = signal<ShoppingListResponseModel | null>(null);
  isLoading = signal(true);
  isExporting = signal(false);
  error = signal<string | null>(null);

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.listId.set(+id);
        this.loadList();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadList(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.shoppingService
      .getShoppingList(this.listId())
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (list: ShoppingListResponseModel) => {
          this.list.set(list);
        },
        error: (err: unknown) => {
          this.error.set(ERROR_MESSAGES.SHOPPING.LOAD_FAILED);
          console.error('Error loading shopping list:', err);
        },
      });
  }

  onExportPDF(): void {
    this.isExporting.set(true);
    const list = this.list();
    if (!list) return;

    // Create print-friendly content
    const printWindow = window.open('', '_blank');
    if (!printWindow) {
      this.notificationService.error('Unable to open print window');
      this.isExporting.set(false);
      return;
    }

    const html = this.generatePrintHTML(list);
    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.print();

    this.isExporting.set(false);
    this.notificationService.success('Shopping list ready to print/save as PDF');
  }

  onExportCSV(): void {
    this.isExporting.set(true);
    const list = this.list();
    if (!list || !list.items) {
      this.notificationService.error('No items to export');
      this.isExporting.set(false);
      return;
    }

    const csv = this.generateCSV(list);
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);

    link.setAttribute('href', url);
    link.setAttribute('download', `shopping-list-${list.name}.csv`);
    link.style.visibility = 'hidden';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    this.isExporting.set(false);
    this.notificationService.success('Shopping list exported to CSV');
  }

  onEmailList(): void {
    const list = this.list();
    if (!list) return;

    const subject = encodeURIComponent(`Shopping List: ${list.name}`);
    const body = encodeURIComponent(this.generateEmailBody(list));
    window.location.href = `mailto:?subject=${subject}&body=${body}`;
  }

  onBack(): void {
    this.router.navigate(['/shopping', this.listId()]);
  }

  private generatePrintHTML(list: ShoppingListResponseModel): string {
    const items = list.items || [];
    const itemsHtml = items
      .map((item: IShoppingListItemModel) => {
        const quantity = item.quantity ? `${item.quantity} ${item.measurementUnit || ''}` : '';
        const checked = item.isCompleted ? '☑' : '☐';
        return `
          <tr>
            <td>${checked}</td>
            <td>${item.name}</td>
            <td>${quantity}</td>
            <td>${item.categoryName || ''}</td>
            <td>${item.notes || ''}</td>
          </tr>
        `;
      })
      .join('');

    return `
      <!DOCTYPE html>
      <html>
        <head>
          <title>${list.name}</title>
          <style>
            body {
              font-family: Arial, sans-serif;
              padding: 20px;
            }
            h1 {
              font-size: 24px;
              margin-bottom: 10px;
            }
            p {
              color: #666;
              margin-bottom: 20px;
            }
            table {
              width: 100%;
              border-collapse: collapse;
            }
            th, td {
              border: 1px solid #ddd;
              padding: 8px;
              text-align: left;
            }
            th {
              background-color: #f2f2f2;
              font-weight: bold;
            }
            @media print {
              body {
                padding: 0;
              }
            }
          </style>
        </head>
        <body>
          <h1>${list.name}</h1>
          <p>${list.description || ''}</p>
          <table>
            <thead>
              <tr>
                <th>Done</th>
                <th>Item</th>
                <th>Quantity</th>
                <th>Category</th>
                <th>Notes</th>
              </tr>
            </thead>
            <tbody>
              ${itemsHtml}
            </tbody>
          </table>
        </body>
      </html>
    `;
  }

  private generateCSV(list: ShoppingListResponseModel): string {
    const items = list.items || [];
    const header = 'Done,Item,Quantity,Unit,Category,Notes\n';
    const rows = items
      .map((item: IShoppingListItemModel) => {
        const done = item.isCompleted ? 'Yes' : 'No';
        const name = `"${item.name.replace(/"/g, '""')}"`;
        const quantity = item.quantity || '';
        const unit = `"${(item.measurementUnit || '').replace(/"/g, '""')}"`;
        const category = `"${(item.categoryName || '').replace(/"/g, '""')}"`;
        const notes = `"${(item.notes || '').replace(/"/g, '""')}"`;
        return `${done},${name},${quantity},${unit},${category},${notes}`;
      })
      .join('\n');

    return header + rows;
  }

  private generateEmailBody(list: ShoppingListResponseModel): string {
    const items = list.items || [];
    const itemsList = items
      .map((item: IShoppingListItemModel) => {
        const quantity = item.quantity ? `${item.quantity} ${item.measurementUnit || ''}` : '';
        const checkbox = item.isCompleted ? '[x]' : '[ ]';
        return `${checkbox} ${item.name} ${quantity}`;
      })
      .join('\n');

    return `${list.name}\n\n${list.description || ''}\n\n${itemsList}`;
  }
}
